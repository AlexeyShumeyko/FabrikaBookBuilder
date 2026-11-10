using System;
using System.Text.Json;

namespace PhotoBook.Application.Sites
{
    /// <summary>
    /// The answer of one call to a site: either the document, or why there is none.
    /// </summary>
    /// <remarks>
    /// The document is owned by the caller, who disposes it. A failure carries a
    /// <see cref="SiteApiError"/> rather than only a sentence, so a caller can tell a session
    /// that ended from a request that was refused, without reading the text.
    /// </remarks>
    public readonly struct SiteApiResult
    {
        private SiteApiResult(bool isSuccess, JsonDocument? document, SiteApiError? error)
        {
            IsSuccess = isSuccess;
            Document = document;
            Error = error;
        }

        public bool IsSuccess { get; }

        /// <summary>The site's answer. Null on failure.</summary>
        public JsonDocument? Document { get; }

        /// <summary>Why the call failed. Null on success.</summary>
        public SiteApiError? Error { get; }

        public static SiteApiResult Ok(JsonDocument document)
        {
            if (document is null)
                throw new ArgumentNullException(nameof(document));

            return new SiteApiResult(true, document, null);
        }

        public static SiteApiResult Fail(SiteApiError error)
        {
            if (error is null)
                throw new ArgumentNullException(nameof(error));

            return new SiteApiResult(false, null, error);
        }

        public static SiteApiResult Fail(SiteApiErrorKind kind, string message, int? statusCode = null) =>
            Fail(new SiteApiError(kind, message, statusCode));
    }
}
