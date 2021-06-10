using System;
using System.Net;

namespace OkkeiPatcher.Model.Exceptions
{
	public class HttpStatusCodeException : Exception
	{
		public HttpStatusCodeException(HttpStatusCode statusCode)
		{
			StatusCode = statusCode;
		}

		public HttpStatusCode StatusCode { get; }
	}
}