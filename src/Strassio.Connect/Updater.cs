using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using Strassio.Licensing;

namespace Strassio.Connect
{
    /// <summary>
    /// Автообновление: Strassio.Connect.exe UPDATE &lt;url&gt; &lt;sha256&gt; &lt;версия&gt; &lt;папка&gt;.
    /// Плагин запускает копию программы из папки обновлений (не из папки аддона — её перезапишет
    /// установщик) и больше в обновление не вмешивается. Весь путь — <see cref="AutoUpdate.Run"/>.
    /// </summary>
    internal static class Updater
    {
        public static int Run(string url, string sha256, string version, string directory)
        {
            // Одно обновление за раз: докер в двух версиях CorelDRAW мог нажать «Обновить» дважды.
            using (var single = new Mutex(false, @"Local\Strassio.AutoUpdate"))
            {
                bool owned;
                try
                {
                    owned = single.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    owned = true;
                }

                if (!owned)
                {
                    return 0;
                }

                try
                {
                    var env = new UpdateEnvironment(
                        Process.GetCurrentProcess().Id,
                        Download,
                        IsCorelRunning,
                        StartInstaller,
                        Thread.Sleep,
                        () => DateTime.UtcNow);
                    return AutoUpdate.Run(url, sha256, version, directory, env);
                }
                finally
                {
                    single.ReleaseMutex();
                }
            }
        }

        /// <summary>Скачивание из отдельной программы: брандмауэр CorelDRAW её не касается, а файл без «метки интернета» — SmartScreen не мешает.</summary>
        private static void Download(Uri uri, string path)
        {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 60000;
            request.UserAgent = "Strassio.Connect";
            request.AllowAutoRedirect = true; // GitHub отдаёт файл через перенаправление на свой склад

            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.ResponseUri == null || response.ResponseUri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException("not https: " + response.ResponseUri);
                }

                using (Stream body = response.GetResponseStream() ?? Stream.Null)
                using (FileStream file = File.Create(path))
                {
                    body.CopyTo(file);
                }
            }
        }

        /// <summary>CorelDRAW любой версии: процесс у всех называется CorelDRW.exe.</summary>
        private static bool IsCorelRunning()
        {
            Process[] found = Process.GetProcessesByName("CorelDRW");
            foreach (Process p in found)
            {
                p.Dispose();
            }

            return found.Length > 0;
        }

        /// <summary>Установщику нужны права администратора — Windows сама покажет окно UAC.</summary>
        private static void StartInstaller(string path, string arguments)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty }))
                {
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED: в окне UAC нажали «Нет».
                throw new UpdateCancelledException();
            }
        }
    }
}
