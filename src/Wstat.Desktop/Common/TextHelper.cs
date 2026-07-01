using System.Globalization;

using WMedia = System.Windows.Media;

namespace Wstat.Desktop.Common;

internal static class TextHelper
{
    public static WMedia.FormattedText BuildTruncatedText(
        string text, double availableWidth, WMedia.Typeface typeface,
        double fontSize, WMedia.Brush brush, double pixelsPerDip = 1.25)
    {
        var ft = new WMedia.FormattedText(
            text, CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight, typeface, fontSize, brush, pixelsPerDip);

        if (ft.Width > availableWidth)
        {
            const string ellipsis = "\u2026";
            var dotWidth = new WMedia.FormattedText(
                ellipsis, CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight, typeface, fontSize, brush, pixelsPerDip).Width;

            var maxText = availableWidth - dotWidth;
            if (maxText <= 0) return ft;

            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                var test = new WMedia.FormattedText(
                    text[..mid], CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, typeface, fontSize, brush, pixelsPerDip);
                if (test.Width <= maxText)
                    lo = mid;
                else
                    hi = mid - 1;
            }

            if (lo > 0)
            {
                ft = new WMedia.FormattedText(
                    text[..lo] + ellipsis, CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, typeface, fontSize, brush, pixelsPerDip);
            }
        }

        return ft;
    }
}
