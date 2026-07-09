using System.Collections.Generic;
using System.Linq;
using NovaFiches.PdfSharpEngine;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// Verifie le tri de l'annexe photo : les photos liees a un point du rapport doivent
/// passer en premier, dans l'ordre d'apparition de ce point (OrderKey croissant), et les
/// photos non liees doivent garder leur ordre d'origine (comportement actuel preserve).
/// </summary>
public class PhotoAppendixRendererTests
{
    private static PhotoAppendixRenderer.PhotoItem Item(string name, int? orderKey) =>
        new("Implantation / ligne de ref", name, "", "data:image/jpeg;base64,AA==", orderKey.HasValue ? "PI." + orderKey : "", orderKey);

    [Fact]
    public void SortForAppendix_LinkedPhotosFirst_InReportOrder()
    {
        var input = new List<PhotoAppendixRenderer.PhotoItem>
        {
            Item("unlinked-1", null),
            Item("linked-late", 5),
            Item("unlinked-2", null),
            Item("linked-early", 1),
        };

        var sorted = PhotoAppendixRenderer.SortForAppendix(input);

        Assert.Equal(
            new[] { "linked-early", "linked-late", "unlinked-1", "unlinked-2" },
            sorted.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void SortForAppendix_NoLinkedPhotos_PreservesOriginalOrder()
    {
        var input = new List<PhotoAppendixRenderer.PhotoItem>
        {
            Item("first", null),
            Item("second", null),
            Item("third", null),
        };

        var sorted = PhotoAppendixRenderer.SortForAppendix(input);

        Assert.Equal(
            new[] { "first", "second", "third" },
            sorted.Select(p => p.Name).ToArray());
    }

    private static PhotoAppendixRenderer.PhotoItem Coords(double? x, double? y, double? z) =>
        new("Implantation / ligne de ref", "photo", "", "data:image/jpeg;base64,AA==", "PI.1", 0, x, y, z);

    // Un point peut n'avoir que X/Y, que Z, les trois, ou aucune coordonnee exploitable :
    // seule chaque composante presente doit apparaitre, jamais un "0.000" fabrique.
    [Theory]
    [InlineData(667471.863, 6874347.222, 77.242, "X 667471.863   Y 6874347.222   Z 77.242")]
    [InlineData(667471.863, 6874347.222, null, "X 667471.863   Y 6874347.222")]
    [InlineData(null, null, 77.242, "Z 77.242")]
    [InlineData(null, null, null, "")]
    public void FormatCoordLine_ShowsOnlyPresentComponents(double? x, double? y, double? z, string expected)
    {
        Assert.Equal(expected, PhotoAppendixRenderer.FormatCoordLine(Coords(x, y, z)));
    }
}
