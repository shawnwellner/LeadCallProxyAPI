namespace RMI.LeadCallProxyAPI {
    public class ProxySettings {
        public int MaxRequestErrorCount { get; set; }
        public int ResetPauseMinutes { get; set; }
        public bool PassThru { get; set; }

        public IPQSSettings IPQS { get; set; }
        public LeadConduitSettings LeadConduit { get; set; }
    }

    public class ProxySettingsBase {
        public ProxySettingsBase() {
            this.RequestTimeoutSeconds = 30;
        }

        public string BaseUrl { get; set; }
        public string ProxyUrl { get; set; }
        public Uri ProxyUri => new Uri(this.ProxyUrl ?? this.BaseUrl);
        public string Method { get; set; }
        public string ContentType { get; set; }
        public string DataTable { get; set; }
        public bool IsPrimary { get; set; }
        public int RequestTimeoutSeconds { get; set; }

        public DateTime ResponseTime { get; set; }
        public DateTime RequestTime { get; set; }
        public string ResponseString { get; set; }
        public double ExecuteTimeSeconds { get; set; }
    }

    public class IPQSSettings : ProxySettingsBase {
        public int MaxFraudScore { get; set; }
        public int SplitPercent { get; set; }
    }

    public class LeadConduitSettings : ProxySettingsBase {

    }

    public class AppSettings() {
        public ProxySettings ProxySettings { get; set; }
    }
}