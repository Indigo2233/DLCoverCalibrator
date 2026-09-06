using System;
using System.Drawing;

namespace DarkLight.CoverCalibrator
{
    internal static class SetupDialogLayout
    {
        internal static Size FitWindowToWorkingArea(
            Size desiredSize,
            Size workingAreaSize,
            int margin,
            Size minimumUsableSize)
        {
            if (margin < 0) throw new ArgumentOutOfRangeException(nameof(margin));

            int availableWidth = Math.Max(1, workingAreaSize.Width - (margin * 2));
            int availableHeight = Math.Max(1, workingAreaSize.Height - (margin * 2));

            int minimumWidth = Math.Min(minimumUsableSize.Width, availableWidth);
            int minimumHeight = Math.Min(minimumUsableSize.Height, availableHeight);

            return new Size(
                Math.Max(minimumWidth, Math.Min(desiredSize.Width, availableWidth)),
                Math.Max(minimumHeight, Math.Min(desiredSize.Height, availableHeight)));
        }
    }
}
