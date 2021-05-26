using System;
using System.Net;

namespace OkkeiPatcher.Model.Exceptions
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