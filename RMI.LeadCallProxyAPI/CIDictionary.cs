namespace RMI.LeadCallProxyAPI {
    public class CIDictionary : Dictionary<string, object> {
        public CIDictionary() : base(StringComparer.InvariantCultureIgnoreCase) { }

        public T GetValue<T>(params string[] keys) {
            foreach(string key in keys) {
                if(this.ContainsKey(key)) {
                    return (T)this[key];
                }
            }
            return default(T);
        }
    }
}
