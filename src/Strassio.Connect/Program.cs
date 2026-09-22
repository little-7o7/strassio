using System;
using System.IO;
using System.Net;
using System.Text;

namespace Strassio.Connect
{
    /// <summary>
    /// Связь плагина с сервером лицензий в отдельном процессе. Часто CorelDRAW запрещают выходить в
    /// интернет правилом брандмауэра («Block CorelDraw Outbound») — плагин живёт внутри CorelDRAW и
    /// попадает под запрет. Эта программа — не CorelDRAW, поэтому её запрос проходит.
    ///
    /// Вызов: Strassio.Connect.exe GET|POST &lt;url&gt;; тело POST — в stdin (UTF-8).
    /// Ответ в stdout (UTF-8): первая строка — код HTTP, дальше тело. Сетевая ошибка — строка
    /// «ERR &lt;описание&gt;» и код выхода 2. Ходит только на https (и http://localhost для проверки).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length != 2 || !(args[0] == "GET" || args[0] == "POST") || !Uri.TryCreate(args[1], UriKind.Absolute, out Uri? uri) || !Allowed(uri))
                {
                    return Write("ERR usage: Strassio.Connect.exe GET|POST https://…", 1);
                }

                EnableModernTls();
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.Method = args[0];
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.UserAgent = "Strassio.Connect";
                if (args[0] == "POST")
                {
                    byte[] body = ReadAll(Console.OpenStandardInput());
                    request.ContentType = "application/json; charset=utf-8";
                    request.ContentLength = body.Length;
                    using (Stream s = request.GetRequestStream())
                    {
                        s.Write(body, 0, body.Length);
                    }
                }

                HttpWebResponse response;
                try
                {
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse error)
                {
                    response = error; // 4xx/5xx — это тоже ответ сервера
                }

                using (response)
                using (var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                {
                    return Write((int)response.StatusCode + "\n" + reader.ReadToEnd(), 0);
                }
            }
            catch (Exception ex)
            {
                var text = new StringBuilder("ERR ");
                for (Exception? e = ex; e != null; e = e.InnerException)
                {
                    text.Append(e == ex ? string.Empty : " → ").Append(e.GetType().Name).Append(": ").Append(e.Message.Replace("\r", " ").Replace("\n", " "));
                }

                return Write(text.ToString(), 2);
            }
        }

        private static bool Allowed(Uri uri) =>
            uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

        private static void EnableModernTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; // TLS 1.3
            }
            catch (NotSupportedException)
            {
                // TLS 1.3 есть не во всех Windows — хватит и TLS 1.2.
            }
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        /// <summary>Пишем байты UTF-8 прямо в stdout: у программы без окна кодировку консоли не поменять.</summary>
        private static int Write(string text, int exitCode)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            using (Stream stdout = Console.OpenStandardOutput())
            {
                stdout.Write(bytes, 0, bytes.Length);
            }

            return exitCode;
        }
    }
}
