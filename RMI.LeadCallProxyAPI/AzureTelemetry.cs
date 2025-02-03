using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Primitives;

namespace RMI.LeadCallProxyAPI {
    public static class AzureTelemetry {
        //private const string RMI_TelemetryRequest = "RMI_TelemetryRequest";
        //private const string RMI_TelemetryOperation = "RMI_TelemetryOperation";

        private static readonly TelemetryConfiguration telemetryConfiguration;
        private static readonly TelemetryClient telemetryClient;

        static AzureTelemetry() {
//#if !DEBUG
            telemetryConfiguration = TelemetryConfiguration.CreateDefault();
            telemetryClient = new TelemetryClient(telemetryConfiguration);
//#endif
        }

        public static void SetTelemetry(HttpContext context) {
            if(telemetryConfiguration == null || telemetryClient == null) {
                return;
            }
            ArgumentNullException.ThrowIfNull(context);
            HttpRequest req = context.Request;
            Uri uri = req.GetRealUri();

            var telemetry = new RequestTelemetry() {
                Name = $"{req.Method} {uri.PathAndQuery}",
                Url = uri
            };

            if(req.Headers.TryGetValue("Request-Id", out StringValues requestId)) {
                telemetry.Context.Operation.Id = GetOperationId(requestId);
                telemetry.Context.Operation.ParentId = requestId;
            }

            /*var requestId = req.Headers.Get("Request-Id");
            // Get the operation ID from the Request-Id (if you follow the HTTP Protocol for Correlation).
            telemetry.Context.Operation.Id = GetOperationId(requestId);
            telemetry.Context.Operation.ParentId = requestId;*/

            string authKey = req.Headers.GetValue("AuthKey");
            string body = req.GetRequestBody().Result;

            var operation = telemetryClient.StartOperation(telemetry);

            telemetry.Properties.Add("Auth-Key", authKey);
            telemetry.Properties.Add("Site-Url", req.GetRealUrl());
            telemetry.Properties.Add("Slot", Settings.SlotName);
            telemetry.Properties.Add("User-IP", req.UserIPAddress());
            telemetry.Properties.Add("User-Agent", req.Headers.UserAgent);

            if(req.Headers?.Count > 0) {
                telemetryClient.TrackEvent("Headers", req.Headers as Dictionary<string, string>);
            }

            if(req.Cookies?.Count > 0) {
                telemetryClient.TrackEvent("Cookies", req.Cookies.ToDictionary());
            }

            var bodyDict = body.ToDictionary();
            if(bodyDict?.Count > 0) {
                telemetryClient.TrackEvent("Body", bodyDict);
            }

            //context.Items.Add(RMI_TelemetryOperation, operation);
            //context.Items.Add(RMI_TelemetryRequest, telemetry);
            
            telemetryClient.TrackRequest(telemetry);
            return;
        }
        private static string GetOperationId(string id) {
            // Returns the root ID from the '|' to the first '.' if any.
            int rootEnd = id.IndexOf('.');
            if(rootEnd < 0) {
                rootEnd = id.Length;
            }
            int rootStart = id[0] == '|' ? 1 : 0;
            return id.Substring(rootStart, rootEnd - rootStart);
        }

        internal static bool IsTelemetryEnabled => telemetryClient != null;

        internal static void TrackEvent(string eventName, IDictionary<string, string> properties) {
            telemetryClient?.TrackEvent(eventName, properties);
        }

        internal static void TrackMetric(string name, double? value) {
            if(value.HasValue) {
                telemetryClient?.TrackMetric(name, value.Value);
            }
        }

        internal static void TrackException(Exception ex) {
            telemetryClient?.TrackException(ex);
        }
    }
}
