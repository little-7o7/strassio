#nullable enable
using System;
using Microsoft.Win32;
using Strassio.Licensing;

namespace Strassio.Corel
{
    /// <summary>
    /// Копия метки времени лицензии (TimeGuard) в реестре текущего пользователя — второе место
    /// рядом с файлом: чтобы откатить часы, пришлось бы найти и удалить обе.
    /// </summary>
    internal sealed class RegistryMarkStore : IMarkStore
    {
        private const string KeyPath = @"Software\Strassio";
        private const string ValueName = "cache";

        public string? Read()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
                return key?.GetValue(ValueName) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Write(string value)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
                key.SetValue(ValueName, value, RegistryValueKind.String);
            }
            catch (Exception)
            {
                // Нет прав — остаётся копия в файле.
            }
        }
    }
}
