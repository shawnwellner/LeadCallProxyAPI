using Newtonsoft.Json;
using System.Globalization;
using System.Reflection;

namespace RMI.LeadCallProxyAPI {
    internal static class Settings {
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        private static readonly TimeZoneInfo _defaultTimeZone;
        private static readonly IFormatProvider _defaultFormatProvider;
        private static Assembly assembly = Assembly.GetExecutingAssembly();

        static Settings() {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings {
                Formatting = Formatting.None,
                DateFormatHandling = DateFormatHandling.IsoDateFormat
            };
            _defaultTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            _defaultFormatProvider = new CultureInfo("en-US");
        }
        public static void Initialize(this IConfiguration configuration) {
            var settings = new AppSettings();
            configuration ??= Configuration;

            configuration.Bind(settings);

            string name = settings.ProxySettings.IPQS.BaseUrl.RegExReplace("[{}]", "");
            settings.ProxySettings.IPQS.BaseUrl = configuration.GetValue<string>(name);

            name = settings.ProxySettings.LeadConduit.BaseUrl.RegExReplace("[{}]", "");
            settings.ProxySettings.LeadConduit.BaseUrl = configuration.GetValue<string>(name);

            ProxySettings = settings.ProxySettings;
            Configuration = configuration;
        }

        private static bool? _isUTC = null;
        public static bool IsUTC {
            get {
                if(_isUTC == null) {
                    const string UTC_ID = "UTC";
                    _isUTC = TimeZoneInfo.Local.Id.IsMatch($@"\b{UTC_ID}\b");
                }
                return _isUTC == true;
            }
        }

        #region Environment Variables
        private static string _slotName = null;
        public static string SlotName {
            get {
                if(_slotName.IsEmpty()) {
                    string slotName = Environment.GetEnvironmentVariable("WEBSITE_SLOT_NAME");
                    if(slotName.HasValue()) {
                        _slotName = slotName.ToLower();
                    } else {
                        string siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME").RegExEscape();
                        if(siteName.HasValue()) {
                            string hostName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
                            string pattern = $@"^{siteName}\.azurewebsites\.net$";
                            if(hostName.IsMatch(pattern)) {
                                _slotName = "production";
                            }
                            pattern = $@"^{siteName}-([^.]+)\.azurewebsites\.net$";
                            if(hostName.Matches(pattern, out string[] groups)) {
                                _slotName = groups[1];
                            }
                        }
                    }
                }
                return _slotName ?? "localhost";
            }
        }

        private static string _buildVersion = null;
        public static string BuildVersion {
            get {
                if(_buildVersion.IsEmpty()) {
                    Version version = Assembly.GetExecutingAssembly().GetName().Version;
                    _buildVersion = version.ToString();
                }
                return _buildVersion;
            }
        }

        private static string[] _scmURLs = null;
        public static string[] ScmUrls {
            get {
                if(_scmURLs == null) {
                    string hostName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
                    if(hostName.HasValue()) {
                        string scmUrl = hostName.Replace(".azurewebsites.net", ".scm.azurewebsites.net");
                        _scmURLs = [
                            $"https://{scmUrl}",
                            $"https://{scmUrl}/env"
                        ];
                    }
                }
                return _scmURLs;
            }
        }
        #endregion

        private static IConfiguration Configuration { get; set; }
        public static ProxySettings ProxySettings { get; private set; }

        public static TimeZoneInfo DefaultTimeZone => _defaultTimeZone;
        public static IFormatProvider DefaultFormatProvider => _defaultFormatProvider;
        public static string AssemblyNameSpace => assembly.GetName().Name;

        private static string _lastUpdated = null;
        public static string LastUpdated {
            get {

                if(_lastUpdated == null) {
                    DateTime lastWriteTime = new FileInfo(assembly.Location).LastWriteTime;
                    if(IsUTC) {
                        lastWriteTime = DateTime.SpecifyKind(lastWriteTime, DateTimeKind.Utc);
                        lastWriteTime = TimeZoneInfo.ConvertTimeFromUtc(lastWriteTime, DefaultTimeZone);
                    }
                    _lastUpdated = lastWriteTime.FullFormat();
                }
                return _lastUpdated;
            }
        }

        public static string AzureUserAgentPattern => Configuration.GetValue<string>("AzureUserAgentPattern");
        public static string AuthKey => Configuration.GetValue<string>("AuthKey");

        public static string Datastore_AuthKey => Environment.GetEnvironmentVariable("Datastore_AuthKey") ??
            Configuration.GetValue<string>("Datastore_AuthKey");

        public static string Pause_AlertChannel => Configuration.GetValue<string>("Pause_AlertChannel");
        public static string SlackUrl => Configuration.GetValue<string>("SlackUrlLocal") ??
            Configuration.GetValue<string>("SlackUrl") ??
            Configuration.GetValue<string>("SlackApiUrl");

        public static string DataQueueApiUrl => Configuration.GetValue<string>("DataQueueApiUrl");
        
        private static Uri _dataQueueApiUri = null;
        public static Uri DataQueueApiUri {
            get {
                if(_dataQueueApiUri == null) {
                    _dataQueueApiUri = new Uri(DataQueueApiUrl);
                }
                return _dataQueueApiUri;
            }
        }
    }
}
