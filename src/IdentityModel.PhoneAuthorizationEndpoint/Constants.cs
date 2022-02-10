﻿namespace IdentityModel.Client
{
    internal class Constants
    {
        public sealed class AnonnymousAuthorizationResponse
        {
            public const string AnonnymousCode = "anonnymous_code";
            public const string UserCode = "user_code";
            public const string VerificationUri = "verification_uri";
            public const string ActivationUri = "activation_uri";
            public const string ActivationUriComplete = "activation_uri_complete";
            public const string ExpiresIn = "expires_in";
            public const string Interval = "interval";
        }
        public sealed class AnonnymousAuthorizationRequest
        {
            public const string RedirectUri = "redirect_uri";
            public const string State = "state";
            public const string Provider = "provider";
            public const string Transport = "transport";
            public const string TransportData = "transport_data";
        }
    }
}
