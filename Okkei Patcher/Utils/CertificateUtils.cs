﻿using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Android.App;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Utils
{
	internal static class CertificateUtils
	{
		private static byte[] ReadCertificate(Stream certStream, int size)
		{
			if (certStream == null) throw new ArgumentNullException(nameof(certStream));
			var data = new byte[size];
			size = certStream.Read(data, 0, size);
			certStream.Close();
			return data;
		}

		public static X509Certificate2 GetSigningCertificate()
		{
			var assets = Application.Context.Assets;
			var testkeyFile = assets?.Open(CertFileName);
			var testkeySize = 2797;
			var testkey = new X509Certificate2(ReadCertificate(testkeyFile, testkeySize), CertPassword);
			testkeyFile?.Close();
			testkeyFile?.Dispose();
			return testkey;
		}
	}
}