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
}
