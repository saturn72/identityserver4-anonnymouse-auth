﻿using IdentityServer4.Extensions;
using IdentityServer4.Models;
using IdentityServer4.Anonymous.Validation;
using IdentityServer4.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using IdentityServer4.Anonymous.Stores;
using IdentityServer4.Anonymous.Transport;

namespace IdentityServer4.Anonymous.Services.Generators
{
    public class AuthorizationResponseGenerator : IAuthorizationResponseGenerator
    {
        #region fields
        private readonly IUserCodeService _userCodeService;
        private readonly AnonymousAuthorizationOptions _options;
        private readonly IAnonymousCodeStore _codeStore;
        private readonly IEnumerable<ITransporter> _transports;
        private readonly ISystemClock _clock;
        private readonly IHandleGenerationService _handleGenerationService;
        private readonly ILogger<AuthorizationResponseGenerator> _logger;
        #endregion

        #region ctor
        public AuthorizationResponseGenerator(
            IUserCodeService userCodeService,
            IOptions<AnonymousAuthorizationOptions> options,
            IAnonymousCodeStore codeStore,
            IEnumerable<ITransporter> transports,
            ISystemClock clock,
            IHandleGenerationService handleGenerationService,
            ILogger<AuthorizationResponseGenerator> logger)
        {
            _userCodeService = userCodeService;
            _options = options?.Value;
            _codeStore = codeStore;
            _transports = transports;
            _clock = clock;
            _handleGenerationService = handleGenerationService;
            _logger = logger;

        }
        #endregion
        public async Task<AuthorizationResponse> ProcessAsync(
            AuthorizationRequestValidationResult validationResult)
        {
            _logger.LogInformation("start Processing anonymous request");
            if (validationResult == null) throw new ArgumentNullException(nameof(validationResult));

            var validatedRequest = validationResult.ValidatedRequest;
            var client = validatedRequest?.Client;
            if (client == null) throw new ArgumentNullException(nameof(validationResult.ValidatedRequest.Client));

            _logger.LogDebug("Creating response for anonymous authorization request");
            var response = new AuthorizationResponse();

            var verificationCode = await _handleGenerationService.GenerateAsync();
            _logger.LogDebug($"anonymous-code was generated valued: {verificationCode}");
            response.VerificationCode = verificationCode;
            // generate activation URIs
            response.VerificationUri = _options.VerificationUri;
            response.VerificationUriComplete = $"{_options.VerificationUri.RemoveTrailingSlash()}?{Constants.UserInteraction.VerificationCode}={response.VerificationCode}";

            // lifetime
            response.Lifetime = client.TryGetIntPropertyOrDefault(_options.LifetimePropertyName, _options.DefaultLifetime);
            _logger.LogDebug($"anonymous lifetime was set to {response.Lifetime}");

            // interval
            response.Interval = _options.Interval;
            _logger.LogDebug($"anonymous interval was set to {response.Interval}");

            //allowed retries
            var allowedRetries = client.TryGetIntPropertyOrDefault(_options.AllowedRetriesPropertyName, _options.AllowedRetries);
            _logger.LogDebug($"Max allowed retries was set to {allowedRetries}");

            var userCode = await GenerateUserCodeAsync(client.UserCodeType ?? _options.DefaultUserCodeType);
            var ac = new AnonymousCodeInfo
            {
                AllowedRetries = allowedRetries,
                ClientId = client.ClientId,
                CreatedOnUtc = _clock.UtcNow.UtcDateTime,
                Description = validatedRequest.Description,
                Lifetime = response.Lifetime,
                ReturnUrl = validatedRequest.RedirectUrl,
                RequestedScopes = validatedRequest.RequestedScopes,
                UserCode = userCode.Sha256(),
                Transport = validatedRequest.Transport,
                VerificationCode = response.VerificationCode,
            };
            _logger.LogDebug($"storing anonymous-code in database: {ac.ToJsonString()}");
            _ = _codeStore.StoreAnonymousCodeInfoAsync(response.VerificationCode, ac);

            _logger.LogDebug("Send code via transports");
            var codeContext = new UserCodeTransportContext
            {
                Transport = validatedRequest.Transport,
                Data = validatedRequest.TransportData,
                Provider = validatedRequest.Provider,
            };
            codeContext.Body = await BuildMessageBody(client, userCode, codeContext);
            _ = _transports.Transport(codeContext);
            return response;
        }
        private Task<string> BuildMessageBody(Client client, string code, UserCodeTransportContext ctx)
        {
            if (!client.Properties.TryGetValue($"formats:{ctx.Transport}", out var msgFormat))
            {
                msgFormat = ctx.Transport switch
                {
                    Constants.TransportTypes.Email =>
                        client.Properties.TryGetValue(_options.DefaultUserCodeEmailFormatPropertyName, out var v) ?
                            v :
                            _options.DefaultUserCodeEmailFormat,
                    Constants.TransportTypes.Sms =>
                        client.Properties.TryGetValue(_options.DefaultUserCodeSmSFormatPropertyName, out var v) ?
                            v :
                            _options.DefaultUserCodeSmsFormat,
                    _ => throw new InvalidOperationException($"Cannot find message format \'{ctx.Transport}\' for client \'{client.ClientName}\'"),
                };
            }

            var body = msgFormat.Replace(Constants.Formats.Fields.UserCode, code);
            return Task.FromResult(body);
        }
        private async Task<string> GenerateUserCodeAsync(string userCodeType)
        {
            var userCodeGenerator = await _userCodeService.GetGenerator(userCodeType);
            var retryCount = 0;

            while (retryCount < userCodeGenerator.RetryLimit)
            {
                var userCode = await userCodeGenerator.GenerateAsync();
                var stored = await _codeStore.FindByUserCodeAsync(userCode, false);
                if (stored == null)
                    return userCode;
                retryCount++;
            }
            throw new InvalidOperationException("Unable to create unique user-code for anonymous flow");
        }
    }

}