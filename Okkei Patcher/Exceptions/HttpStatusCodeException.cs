using System;
using System.Net;

namespace OkkeiPatcher.Exceptions
{
	internal class HttpStatusCodeException : Exception
	{
		public HttpStatusCodeException(HttpStatusCode statusCode)
		{
			StatusCode = statusCode;
		}

		public HttpStatusCode StatusCode { get; }
	}
}