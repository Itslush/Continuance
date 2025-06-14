﻿namespace Continuance.Roblox.Http
{
    public static class HttpRequestMessageExtensions
    {
        public static HttpRequestMessage Clone(this HttpRequestMessage req)
        {
            HttpRequestMessage clone = new(req.Method, req.RequestUri);

            if (req.Content != null)
            {
                var ms = new MemoryStream();
                req.Content.CopyToAsync(ms).GetAwaiter().GetResult();
                ms.Position = 0;
                clone.Content = new StreamContent(ms);

                if (req.Content.Headers != null)
                {
                    foreach (var h in req.Content.Headers)
                    {
                        clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    }
                }
            }

            clone.Version = req.Version;

            #if NET5_0_OR_GREATER

            foreach (KeyValuePair<string, object?> option in req.Options)
            {
                if (option.Value != null) clone.Options.TryAdd(option.Key, option.Value);
            }

            #endif

            foreach (KeyValuePair<string, IEnumerable<string>> header in req.Headers)
            {
                if (!header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                {
                    clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }
}