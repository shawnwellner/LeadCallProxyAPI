using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Collections;
using System.Collections.Specialized;
using System.Net;
using System.Runtime.Caching;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RMI.LeadCallProxyAPI {
    public static class ExMethods {
        private static readonly ObjectCache _memoryCache = MemoryCache.Default;

        public static bool HasValue<T>(this T value) where T : class {
            if(value is string str) { return !string.IsNullOrWhiteSpace(str); }
            return value != null;
        }

        public static bool IsEmpty(this string value) {
            return string.IsNullOrEmpty(value);
        }

        public static string IsEmpty(this string value, string defaultValue = null) {
            if(value.IsEmpty()) { return defaultValue; }
            return value;
        }

        public static bool ToBool(this string value, bool defautValue = false) {
            return value?.IsMatch("^(true|1|yes)$") ?? defautValue;
        }

        public static int? ToInt(this string value) {
            if(value.HasValue() && int.TryParse(value, out int val)) {
                return val;
            }
            return null;
        }

        public static int ToInt(this string value, int defaultValue) {
            return value.ToInt() ?? defaultValue;
        }

        public static int? ToInt(this StringValues value) {
            string strValue = value.FirstOrDefault();
            if(strValue.HasValue() && int.TryParse(strValue, out int val)) {
                return val;
            }
            return null;
        }

        public static string ToLower(this bool value) {
            return value.ToString().ToLower();
        }

        public static bool In(this Enum value, params Enum[] array) {
            return array.Contains(value);
        }

        public static bool Between(this int value, int min, int max) {
            return value >= min && value <= max;
        }

        public static string Format(this double value) {
            return value.ToString("0.000");
        }

        public static string Format(this double? value) {
            return value?.ToString("0.000 s");
        }

        public static string Format(this TimeSpan value) {
            if(value == TimeSpan.Zero) { return "00:00.000"; }
            if(value.TotalHours >= 1) {
                return string.Format("{0:hh\\:mm\\:ss\\.fff}", value);
            }
            return string.Format("{0:mm\\:ss\\.fff}", value);
        }

        public static string DomainOnly(this string url) {
            if(url.IsEmpty() || url.Contains(',')) { return url; }
            Uri uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Authority}";
        }

        public static StringContent ToStringContent(this string content, string contentType) {
            if(content.IsEmpty()) { return null; }
            return new StringContent(content, Encoding.UTF8, contentType);
        }

        public static StringContent ToJsonContent(this string content) {
            return content.ToStringContent("application/json");
        }

        public static string CompressJson(this string json) {
            var obj = JsonConvert.DeserializeObject(json);
            return JsonConvert.SerializeObject(obj, Formatting.None);
        }

        public static bool EqualsIgnoreCase(this string value, string compare) {
            if(value.IsEmpty() || compare.IsEmpty()) { return false; }
            return value.Equals(compare, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMatch(this string value, string pattern, RegexOptions options = RegexOptions.IgnoreCase) {
            if(value.IsEmpty() || pattern.IsEmpty()) { return false; }
            return Regex.IsMatch(value, pattern, options);
        }

        public static bool Matches(this string value, string pattern, out string[]? groups, RegexOptions options = RegexOptions.IgnoreCase) {
            groups = null;
            if(value.IsEmpty() || pattern.IsEmpty()) { return false; }
            Match match = Regex.Match(value, pattern, options);
            if(match.Success) {
                List<string> lst = new List<string>();
                foreach(Group grp in match.Groups) {
                    lst.Add(grp.Value);
                }
                groups = lst.ToArray();
                return true;
            }
            return false;
        }

        public static Match Match(this string value, string pattern, RegexOptions options = RegexOptions.IgnoreCase) {
            if(value.IsEmpty() || pattern.IsEmpty()) { return null; }
            return Regex.Match(value, pattern, options);
        }

        public static string RegExReplace(this string value, string pattern, string replacement, RegexOptions options = RegexOptions.IgnoreCase) {
            if(value.IsEmpty() || pattern.IsEmpty()) { return value; }
            return Regex.Replace(value, pattern, replacement, options);
        }

        public static string RegExEscape(this string value) {
            if(value.IsEmpty()) { return value; }
            return Regex.Escape(value);
        }

        public static string FullFormat(this DateTime datetime) {
            const string pattern = @"[ a-z]+";
            string suffix;
            TimeZoneInfo tz = Settings.DefaultTimeZone;
            string name;
            if(tz.IsDaylightSavingTime(datetime)) {
                name = tz.DaylightName;
            } else {
                name = tz.StandardName;
            }
            suffix = name.RegExReplace(pattern, "", RegexOptions.None);
            return $"{datetime.ToString(Settings.DateTimeFormat)} {suffix}";
        }

        public static string ToJson<T>(this T instance) {
            if(instance == null) { throw new NullReferenceException(); }
            var settings = new JsonSerializerSettings() {
                Formatting = Formatting.None,
            };

            if(instance is IEnumerable list) {
                var enumerator = list.GetEnumerator();
                if(enumerator.MoveNext()) {
                    var item = enumerator.Current;
                    bool isResolver = item.GetType().IsSubclassOf(typeof(DefaultContractResolver));
                    if(isResolver) {
                        settings.ContractResolver = item as DefaultContractResolver;
                    }
                }
            } else if(instance is DefaultContractResolver resolver) {
                settings.ContractResolver = resolver;
            }

            string json = JsonConvert.SerializeObject(instance, settings);
            if(instance is Exception) {
                JObject jObj = JObject.Parse(json);
                jObj.AddFirst(new JProperty("BuildVersion", Settings.BuildVersion));
                jObj.AddFirst(new JProperty("Environment", Settings.SlotName));
                json = jObj.ToCleanString();
            }
            return json;
        }

        public static string ToCleanString<T>(this T jtoken) where T : JToken {
            const Formatting format = Formatting.None;
            try {
                return jtoken.Type switch {
                    JTokenType.Object => jtoken.ToString(format),
                    JTokenType.Array => jtoken.ToString(format),
                    JTokenType.Bytes => Convert.ToBase64String(jtoken.Value<byte[]>()),
                    JTokenType.String => jtoken.Value<string>(),
                    _ => jtoken.Value<string>()
                };
            } catch {
                return jtoken.ToString(format);
            }
        }

        public static IDictionary<string, object> Clone(this IDictionary list, bool clear) {
            if(list == null) { return null; }
            IDictionary<string, object> clone = new Dictionary<string, object>();
            foreach(string key in list.Keys) {
                clone.Add(key, list[key]);
            }
            if(clear) { list.Clear(); }
            return clone;
        }

        public static T AddSpacer<T>(this T instance) where T : IEnumerable {
            string space = " ";
            if(instance is IDictionary lst1) {
                while(lst1.Contains(space)) {
                    space += " ";
                }
                lst1.Add(space, null);
            } else if(instance is IDictionary<string, object> lst2) {
                while(lst2.ContainsKey(space)) {
                    space += " ";
                }
                lst2.Add(space, null);
            }

            return instance;
        }

        public static T SafeAdd<T>(this T instance, object key, object value) where T : IEnumerable {
            if(instance != null && key.HasValue()) {
                string strKey;
                if(instance is IDictionary iDict) {
                    if(!iDict.Contains(key)) { iDict.Add(key, value); }
                } else if(instance is IDictionary<string, string> strDict) {
                    strKey = Convert.ToString(key);
                    if(!strDict.ContainsKey(strKey)) { strDict.Add(strKey, value?.ToString()); }
                } else if(instance is IDictionary<string, object> dict) {
                    strKey = Convert.ToString(key);
                    if(!dict.ContainsKey(strKey)) { dict.Add(strKey, value); }
                } else if(instance is NameValueCollection collection) {
                    strKey = Convert.ToString(key);
                    if(!collection.AllKeys.Contains(strKey)) {
                        collection.Add(strKey, value?.ToString());
                    }
                } else if(instance is IDictionary<object, object> objDict) {
                    if(!objDict.ContainsKey(key)) { objDict.Add(key, value); }
                }
            }
            return instance;
        }

        public static bool SafeRemove<T>(this T instance, object key) where T : IEnumerable {
            if(key.HasValue() && instance != null) {
                if(instance is IDictionary lst1) {
                    if(lst1.Contains(key)) {
                        lst1.Remove(key);
                        return true;
                    }
                } else if(instance is IDictionary<string, object> lst2) {
                    string strKey = Convert.ToString(key);
                    if(lst2.ContainsKey(strKey)) {
                        lst2.Remove(strKey);
                        return true;
                    }
                }
            }
            return false;
        }

        public static string Join(this IEnumerable<string> values, char separator = ',') {
            return string.Join(separator, values);
        }

        public static IDictionary<string, string> ToDictionary(this string json) {
            if(json.IsEmpty()) { return null; }
            IDictionary<string, string> dict = new Dictionary<string, string>();
            try {
                JToken token = JToken.Parse(json);
                JObject jObj = token as JObject;
                if(jObj != null) {
                    jObj.Properties().ToList()
                        .ForEach(j => dict.Add(j.Name, j.Value.ToString()));
                } else {
                    JArray jArr = token as JArray;
                    if(jArr != null) {
                        int index = 0;
                        foreach(var i in jArr) {
                            if(null != (jObj = i as JObject)) {
                                jObj.Properties().ToList()
                                    .ForEach(j => dict.Add($"{j.Name}[{index}]", j.Value.ToString()));
                            }
                            index++;
                        }
                    }
                }
                if(dict.Count == 0) {
                    dict.Add("Data", json);
                }
            } catch {
                dict.Add("Data", json);
            }
            return dict;
        }

        public static T GetItem<T>(this IDictionary collection, string key) {
            foreach(string k in collection.Keys) {
                if(k.Equals(key, StringComparison.InvariantCultureIgnoreCase)) {
                    return (T)collection[k];
                }
            }
            return default;
        }

        public static string GetValue<T>(this T list, string key, string defaultValue = null) where T : IEnumerable, IHeaderDictionary {
            if(list == null || key.IsEmpty()) { return defaultValue; }
            if(list is NameValueCollection collection) {
                return collection[key] ?? defaultValue;
            } else if(list is IDictionary<string, string> dict) {
                if(dict.TryGetValue(key, out string value)) {
                    return value;
                }
            } else if(list is IHeaderDictionary headers) {
                if(headers.TryGetValue(key, out StringValues value)) {
                    return value.FirstOrDefault();
                }
            }
            return defaultValue;
        }

        public static bool IsCacheRequest(this HttpRequest req) {
            if(req == null) { return false; }
            return req.Method.IsMatch("GET") &&
                   req.Path.Value.IsMatch("/status|clear|reset/?");
        }

        public static string UserIPAddress(this HttpRequest request) {
            string ipAddress = null;
            Uri uri = request.RequestUri();

            if(uri.IsLoopback) {
                ipAddress = "localhost";
            } else if(null == (ipAddress = request.Headers["cf-connecting-ip"])) {
                if(request.Headers.TryGetValue("X-Forwarded-For", out StringValues values)) {
                    ipAddress = values.FirstOrDefault();
                }
                if(ipAddress.IsEmpty()) {
                    ipAddress = request.Headers.GetValue("X-Real-IP", uri.Host);
                }
            }
            return ipAddress ?? request.HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        public static Uri RequestUri(this HttpRequest request) {
            string url = request.GetDisplayUrl();
            return new Uri(url);
        }

        public static Uri GetRealUri(this HttpRequest request) {
            const string KeyName = "RMI-RealUrl";
            Uri uri = request.RequestUri();
            try {
                HttpContext context = request.HttpContext;
                if(!context.Items.TryGetValue(KeyName, out object value)) {
                    string url = uri.Host;
                    if(request.Headers.TryGetValue("X-ORIGINAL-HOST", out StringValues host)) {
                        url = host.FirstOrDefault();
                    }

                    string qs = request.QueryString.ToString();
                    uri = new UriBuilder(url) {
                        Scheme = request.Scheme,
                        Port = uri.Port,
                        Path = request.Path,
                        Query = qs.RegExReplace(@"^\?", "")
                    }.Uri;
                    context.Items.SafeAdd(KeyName, uri);
                }
                return uri;
            } catch {
                return uri;
            }
        }

        public static string GetRealUrl(this HttpRequest request) {
            return request.GetRealUri().AbsoluteUri;
        }

        public static bool IsAzureRequest(this HttpRequest req) {
            string userAgent = req.Headers.UserAgent.ToString();
            if(req.ValidateKeyToken() || userAgent.IsMatch(Settings.AzureUserAgentPattern)) {
                return true;
            }
            return false;
        }

        public static bool ValidateKeyToken(this HttpRequest req) {
            string headerValue;
            if(req.Headers.TryGetValue("x-ms-auth-internal-token", out StringValues values)) {
                headerValue = values.SingleOrDefault();
            } else {
                return false;
            }
            string key = Environment.GetEnvironmentVariable("WEBSITE_AUTH_ENCRYPTION_KEY");
            if(key.IsEmpty()) { return false; }

            SHA256 sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(key);
            string hash = Convert.ToBase64String(sha.ComputeHash(bytes));
            return hash == headerValue;
        }

        public static ProxyControllerEx GetProxyController(this HttpContext context) {
            if(context.Items.TryGetValue(ProxyControllerEx.Name, out object value)) {
                return value as ProxyControllerEx;
            }
            return null;
        }

        public static T GetOptionValue<T>(this HttpRequestMessage req, string key) {
            HttpRequestOptionsKey<T> optionsKey = new HttpRequestOptionsKey<T>(key);
            if(req.Options.TryGetValue<T>(optionsKey, out T item)) {
                return item;
            }
            return default;
        }

        public static string AsString(this HttpStatusCode code) {
            return ((int)code).ToString();
        }

        public static HttpStatusCode ToHttpStatusCode(this WebExceptionStatus code) {
            switch(code) {
                case WebExceptionStatus.Success:
                    return HttpStatusCode.OK;
                case WebExceptionStatus.RequestProhibitedByCachePolicy:
                case WebExceptionStatus.RequestCanceled:
                case WebExceptionStatus.ConnectionClosed:
                case WebExceptionStatus.ConnectFailure:
                    return HttpStatusCode.ServiceUnavailable;
                case WebExceptionStatus.Timeout:
                    return HttpStatusCode.RequestTimeout;
                case WebExceptionStatus.ProtocolError:
                    return HttpStatusCode.BadRequest;
                case WebExceptionStatus.NameResolutionFailure:
                    return HttpStatusCode.NotFound;
                default:
                    return HttpStatusCode.InternalServerError;
            }
        }

        #region Cache Methods
        public static void RemoveCache(this string key) {
            try {
                _memoryCache.Remove(key.ToLower());
            } catch {
                //Ignore Error
            }
        }

        public static IEnumerable<T> GetCachedItems<T>() {
            foreach(var item in _memoryCache) {
                if(item.Value is T) {
                    yield return (T)item.Value;
                }
            }
        }

        public static bool IsCached<T>(this string key) {
            if(key.IsEmpty()) { return false; }
            key = key.ToLower();
            if(_memoryCache.Contains(key)) {
                return _memoryCache[key] is T;
            }
            return false;
        }

        public static bool TryGetCachedItem<T>(this string key, out T cachedItem) {
            T defaultValue = default(T);
            cachedItem = key.Cache<T>();
            return cachedItem != null && !cachedItem.Equals(defaultValue);
        }

        public static T Cache<T>(this string key) {
            T result = default(T);
            try {
                if(key.HasValue()) {
                    key = key.ToLower();
                    object value = _memoryCache.Get(key);
                    if(value != null && value.Equals(result) != true) {
                        try {
                            result = (T)_memoryCache.Get(key);
                        } catch {
                            result = (T)Convert.ChangeType(value, typeof(T));
                        }
                    }
                }
                return result;
            } catch {
                return result;
            }
        }

        public static void CacheUpdate<T>(this string key, T value) {
            key = key.ToLower();
            if(_memoryCache.Contains(key)) {
                _memoryCache[key] = value;
                return;
            }
            throw new NullReferenceException("Key not found in cache");
        }

        public static T Cache<T>(this string key, T value, double expireMinutes) {
            if(key.IsEmpty() || value == null) { return value; }
            key = key.ToLower();
            if(_memoryCache.Contains(key)) {
                _memoryCache[key] = value;
            } else {
                CacheItemPolicy policy = new CacheItemPolicy() {
                    AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(expireMinutes)
                };
                _memoryCache.Set(key, value, policy);
            }
            return value;
        }
        #endregion Cache Methods

        #region Session Data
        public static string GetSessionId(this HttpContext context) {
            const string key = "RMI_SessionId";
            if(context.Items.TryGetValue(key, out object value)) {
                return value as string;
            } else {
                string sessionId = Guid.NewGuid().ToString();
                context.Items.Add(key, sessionId);
                return sessionId;
            }
        }

        public static Dictionary<string, string> GetSessionData(this string sessionId) {
            return sessionId.Cache<Dictionary<string, string>>();
        }

        public static Dictionary<string, string> GetSessionData(this HttpContext context) {
            string sessionId = context.GetSessionId();
            return sessionId.GetSessionData();
        }

        public static string CacheSession(this HttpContext context) {
            var req = context.Request;
            string sessionId = context.GetSessionId();

            var data = new Dictionary<string, string>();

            string postData = null; //req.GetRequestBody().Result;
            string remoteAddress = req.UserIPAddress();
            string url = req.GetRealUri().AbsoluteUri;

            data.SafeAdd("Request-Url", url)
                .SafeAdd("Method", req.Method)
                .SafeAdd("Remote-Address", remoteAddress);

            if(postData.HasValue()) {
                data.AddSpacer()
                    .SafeAdd("PostedData", postData);
            }

            data.SafeAdd(WebRequest.InsertHereKey, string.Empty)
                .AddSpacer()
                .SafeAdd("Headers", "");

            foreach(var header in req.Headers.Where(h => !h.Key.IsMatch("cookie"))) {
                data.SafeAdd($"\t\t- {header.Key}", header.Value.Join());
            }

            sessionId.Cache(data, 5);
            return sessionId;
        }
        #endregion Session Data

        public static RequestTelemetry GetRequestTelemetry(this HttpContext context) {
            return context?.Items["Microsoft.ApplicationInsights.RequestTelemetry"] as RequestTelemetry;
        }

        public static HttpRequest ToHttpRequest(this HttpRequestMessage httpRequestMessage) {
            HttpContext context = new DefaultHttpContext();

            var request = context.Request;

            // Copy method
            request.Method = httpRequestMessage.Method.Method;

            // Copy request URI
            request.Scheme = httpRequestMessage.RequestUri.Scheme;
            request.Host = new HostString(httpRequestMessage.RequestUri.Host, httpRequestMessage.RequestUri.Port);
            request.Path = httpRequestMessage.RequestUri.AbsolutePath;
            request.QueryString = new QueryString(httpRequestMessage.RequestUri.Query);

            // Copy headers
            foreach(var header in httpRequestMessage.Headers) {
                request.Headers[header.Key] = header.Value.ToArray();
            }

            // Copy content headers and body
            if(httpRequestMessage.Content != null) {
                foreach(var header in httpRequestMessage.Content.Headers) {
                    request.Headers[header.Key] = header.Value.ToArray();
                }
                //request.Body = await httpRequestMessage.Content.ReadAsStreamAsync();
            }

            return request;
        }

        public static bool IsAdmin(this HttpRequest request) {
#if DEBUG
            return true;
#else
            const string _IP_PATTERN = @"^172\.16\.10\.\d{1,2}";
            Uri uri = request.RequestUri();
            if(uri.IsLoopback) { return true; }
            if(request.Headers.TryGetValue("X-Forwarded-For", out StringValues values)) {
                return values.Any(v => v.IsMatch(_IP_PATTERN));
            }
            return false;
#endif
        }
    }
}
