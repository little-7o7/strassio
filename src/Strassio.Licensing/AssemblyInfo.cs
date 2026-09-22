using System.Runtime.CompilerServices;

// Классы лицензии внутренние (docs/SPEC.md, 13.7): обфускатор тогда переименовывает их все, и в
// готовой DLL не найти по имени, где проверяется лицензия. Видят их только аддон, тесты и UiShots.
[assembly: InternalsVisibleTo("Strassio.Corel")]
[assembly: InternalsVisibleTo("Strassio.Core.Tests")]
[assembly: InternalsVisibleTo("Strassio.UiShots")]
