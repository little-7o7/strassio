using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Methods;

/// <summary>Картинки-схемы методов в списке докера (docs/SPEC.md, раздел 2.2, пункт 4).</summary>
public class MethodSamplesTests
{
    [Fact]
    public void EveryMethod_HasReadableSample()
    {
        foreach (MethodInfo info in MethodCatalog.All)
        {
            MethodResult result = MethodSamples.Run(info.Kind, out MethodSample _);
            Assert.InRange(result.Stones.Count, 8, 40);

            bool[] overlaps = IntersectionFixer.FindIndicesToRemove(result.Stones, 0.05, 0.01);
            Assert.DoesNotContain(true, overlaps);
        }
    }

    [Fact]
    public void Samples_LookDifferent()
    {
        int Count(MethodKind kind) => MethodSamples.Run(kind, out MethodSample _).Stones.Count;

        // F1 (сетка) и F2 (соты) на одном круге дают разное число кружков — схемы различимы.
        Assert.NotEqual(Count(MethodKind.F1), Count(MethodKind.F2));
    }
}
