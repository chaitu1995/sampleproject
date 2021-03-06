//------------------------------------------------------------------------------
// <copyright file="DpiHelper.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------
// based on \env\shell\PackageFramework\Current\Shell\UI\Common\DpiHelper.cs
// changes: WPF-related code is removed, class is internal, and initialization is done lazily
using System.Configuration;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Runtime.Versioning;

#if Microsoft_NAMESPACE
using System.Windows.Forms.Internal;
using CAPS = System.Windows.Forms.NativeMethods;
#elif DRAWING_NAMESPACE
using CAPS = System.Drawing.SafeNativeMethods;
#elif DRAWINGDESIGN_NAMESPACE
using System.Drawing.Design;
using CAPS = System.Drawing.Design.NativeMethods;
#else
using System.Design;
using CAPS = System.Design.NativeMethods;
#endif

namespace System.Windows.Forms
{
    /// <summary>
    /// Helper class for scaling coordinates and images according to current DPI scaling set in Windows for the primary screen.
    /// </summary>
    internal static partial class DpiHelper
    {
        internal const double LogicalDpi = 96.0;

        private static bool isInitialized = false;

        /// <summary>
        /// The primary screen's (device) current DPI
        /// </summary>
        private static double deviceDpi = LogicalDpi;
        private static double logicalToDeviceUnitsScalingFactor = 0.0;
        private static bool enableHighDpi = false;
        private static string dpiAwarenessValue = null;
        private static InterpolationMode interpolationMode = InterpolationMode.Invalid;
        
        // Following quirks are not used in System.Drawing.  Making sure they light up only in the required binaries.
#if (!DRAWING_NAMESPACE)
        private static bool isDpiHelperQuirksInitialized = false;
        private static bool enableToolStripHighDpiImprovements = false;
        private static bool enableDpiChangedMessageHandling = false;
        private static bool enableCheckedListBoxHighDpiImprovements = false;
        private static bool enableThreadExceptionDialogHighDpiImprovements = false;
        private static bool enableDataGridViewControlHighDpiImprovements = false;
        private static bool enableSinglePassScalingOfDpiForms = false;
        private static bool enableAnchorLayoutHighDpiImprovements = false;
        private static bool enableMonthCalendarHighDpiImprovements = false;

        // 'enableDpiChangedHighDpiImprovements' flag default behaviour is different from the flags defined above. 
        // Explicit opt-in is required if application is not targetting the minimum required framework version.
        private static bool enableDpiChangedHighDpiImprovements = false;
        private static readonly Version dpiChangedMessageHighDpiImprovementsMinimumFrameworkVersion = new Version(4, 8);
#endif        
        private static void Initialize()
        {
            if (isInitialized)
            {
                return;
            }

            if (IsDpiAwarenessValueSet())
            {
                enableHighDpi = true;
            }
            else
            {
                try
                {
                    // For legacy users who define this constant in app settings. But we read it only when we do not see a valid dpiawareness value in the Microsoft section
                    string value = ConfigurationManager.AppSettings.Get(ConfigurationStringConstants.EnableWindowsFormsHighDpiAutoResizingKeyName);
                    if (!string.IsNullOrEmpty(value) && string.Equals(value, "true", StringComparison.InvariantCultureIgnoreCase))
                    {
                        enableHighDpi = true;
                    }
                }
                catch
                {
                }
            }

            if (enableHighDpi)
            {
#if Microsoft_NAMESPACE
                try
                {
                    if (!DpiHelper.SetWinformsApplicationDpiAwareness())
                    {
                        Debug.WriteLine("Failed to set Application DPI awareness");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Failed to set Application DPI awareness " + ex.ToString());
                }
#endif

                IntPtr hDC = UnsafeNativeMethods.GetDC(NativeMethods.NullHandleRef);
                if (hDC != IntPtr.Zero)
                {
                    deviceDpi = UnsafeNativeMethods.GetDeviceCaps(new HandleRef(null, hDC), CAPS.LOGPIXELSX);

                    UnsafeNativeMethods.ReleaseDC(NativeMethods.NullHandleRef, new HandleRef(null, hDC));
                }
            }
            isInitialized = true;
        }

        internal static bool IsDpiAwarenessValueSet()
        {
            bool dpiAwarenessValueSet = false;
            try
            {
                if (string.IsNullOrEmpty(dpiAwarenessValue))
                {
                    dpiAwarenessValue = ConfigurationOptions.GetConfigSettingValue(ConfigurationStringConstants.DpiAwarenessKeyName);
                }
            }
            catch
            {
            }

            if (!string.IsNullOrEmpty(dpiAwarenessValue)) // setting in configuration wins
            {
                var value = dpiAwarenessValue.ToLowerInvariant();
                switch (value)
                {
                    case "true":
                    case "system":
                    case "true/pm":
                    case "permonitor":
                    case "permonitorv2":
                        dpiAwarenessValueSet = true;
                        break;
                    case "false":
                        System.Diagnostics.Debug.WriteLine(" 'DpiAwarenessValue' is set to 'false', value =  " + value);
                        break;
                    default:
                        System.Diagnostics.Debug.WriteLine("Either 'DpiAwarenessValue' is not set or 'DpiAwarenessValue' set is invalid in app.config, value set =  " + value);
                        break;
                }
            }

            return dpiAwarenessValueSet;
        }

        
        // Following quirks and public properties are not used in System.Drawing.  Making sure they light up only in the required binaries.
#if (!DRAWING_NAMESPACE)
        internal static void InitializeDpiHelperForWinforms()
        {
            // initialize shared fields
            Initialize();
            InitializeDpiHelperQuirks();
        }

        internal static void InitializeDpiHelperQuirks()
        { 
            if (isDpiHelperQuirksInitialized)
            {
                return;
            }
            try
            {
                // Redstone 2 or greater, where all APIs required by this feature are available
                if ((Environment.OSVersion.Platform == System.PlatformID.Win32NT) &&
                        (Environment.OSVersion.Version.CompareTo(ConfigurationOptions.RS2Version) >= 0) &&
                        (IsExpectedConfigValue(ConfigurationStringConstants.DisableDpiChangedMessageHandlingKeyName, false)) &&
                        (IsDpiAwarenessValueSet()) &&
                        // The dynamic scaling features are implemented only in comclt32 v6, no point to
                        // activate it otherwise.
                        (Application.RenderWithVisualStyles))
                {
                    // user had not opted out from dynamic scaling level changes but the primary screen DPI might be 96
                    enableDpiChangedMessageHandling = true;
                }

                // IsScalingRequired returns true if the current resolution is not 96DPI on the primary monitor.
                // However PerMonitor DPI aware applicaitons need dynamic scaling initialized properly even if the
                // the current DPI is 96 because they handle DPI change.
                if ((DpiHelper.IsScalingRequired || enableDpiChangedMessageHandling) && IsDpiAwarenessValueSet())
                {
                    if (IsExpectedConfigValue(ConfigurationStringConstants.CheckedListBoxDisableHighDpiImprovementsKeyName, false))
                    {
                        enableCheckedListBoxHighDpiImprovements = true;
                    }

                    if (IsExpectedConfigValue(ConfigurationStringConstants.ToolStripDisableHighDpiImprovementsKeyName, false))
                    {
                        enableToolStripHighDpiImprovements = true;
                    }

                    if (IsExpectedConfigValue(ConfigurationStringConstants.FormDisableSinglePassScalingOfDpiFormsKeyName, false))
                    {
                        enableSinglePassScalingOfDpiForms = true;
                    }

                    if (IsExpectedConfigValue(ConfigurationStringConstants.DataGridViewControlDisableHighDpiImprovements, false))
                    {
                        enableDataGridViewControlHighDpiImprovements = true;
                    }

                    if (IsExpectedConfigValue(ConfigurationStringConstants.AnchorLayoutDisableHighDpiImprovementsKeyName, false))
                    {
                        enableAnchorLayoutHighDpiImprovements = true;
                    }

                    if (IsExpectedConfigValue(ConfigurationStringConstants.MonthCalendarDisableHighDpiImprovementsKeyName, false))
                    {
                        enableMonthCalendarHighDpiImprovements = true;
                    }

                    if (ConfigurationOptions.GetConfigSettingValue(ConfigurationStringConstants.DisableDpiChangedHighDpiImprovementsKeyName) == null)
                    {
                        if (ConfigurationOptions.NetFrameworkVersion.CompareTo(dpiChangedMessageHighDpiImprovementsMinimumFrameworkVersion) >= 0)
                        {
                            enableDpiChangedHighDpiImprovements = true;
                        }
                    }
                    else
                    {
                        if (IsExpectedConfigValue(ConfigurationStringConstants.DisableDpiChangedHighDpiImprovementsKeyName, false))
                        {
                            enableDpiChangedHighDpiImprovements = true;
                        }
                    }

                    // no opt-out switch at the moment
                    enableThreadExceptionDialogHighDpiImprovements = true;
                }
            }
            catch
            {
            }

            isDpiHelperQuirksInitialized = true;
        }

        /// <summary>
        /// Checks if configuration setting is set to expected value
        /// </summary>
        /// <param name="configurationSettingName">Configuration setting name</param>
        /// <param name="expectedValue">Expected value</param>
        /// <returns>true/false</returns>
        internal static bool IsExpectedConfigValue(string configurationSettingName, bool expectedValue)
        {
            string value = ConfigurationOptions.GetConfigSettingValue(configurationSettingName);
            bool valueAsBool;
            if (!bool.TryParse(value, out valueAsBool))
            {
                // neither 'true' nor 'false' - implies 'false'
                valueAsBool = false;
            }

            return valueAsBool == expectedValue;
        }

        /// <summary>
        /// Returns a boolean to specify if we should enable HighDPI improvements added in .NET 4.8
        /// </summary>
        internal static bool EnableDpiChangedHighDpiImprovements
        {
            get
            {
                InitializeDpiHelperForWinforms();
                return enableDpiChangedHighDpiImprovements;
            }
        }

        /// <summary>
        /// Returns a boolean to specify if we should enable ToolStrip HighDPI fixes.
        /// </summary>
        internal static bool EnableToolStripHighDpiImprovements
        {
            get
            {
                InitializeDpiHelperForWinforms();
                return enableToolStripHighDpiImprovements;
            }
        }

        /// <summary>
        /// Returns a boolean to specify if we should enable ToolStrip HighDPI improvements for PerMonitorV2 scenarios.
        /// </summary>
        internal static bool EnableToolStripPerMonitorV2HighDpiImprovements
        {
            get
            {
                // We will get PerMonitorV2 Improvements for the ToolStrip based controls/components, when
                return EnableDpiChangedMessageHandling &&       // we will receive DpiChanged Messages,
                       enableToolStripHighDpiImprovements &&    // when specific ToolStrip HighDpiImrovements AND
                       enableDpiChangedHighDpiImprovements;     // generic DpiChanged HighDPI Improvements (4.8) are OKed.
            }
        }

        /// <summary>
        /// Returns a boolean to specify if we should enable processing of WM_DPICHANGED and related messages
        /// </summary>
        internal static bool EnableDpiChangedMessageHandling
        {
            get
            {
                InitializeDpiHelperForWinforms();
                if (enableDpiChangedMessageHandling)
                {
                    // We can't cache this value because different top level windows can have different DPI awareness context
                    // for mixed mode applications.
                    DpiAwarenessContext dpiAwareness = CommonUnsafeNativeMethods.GetThreadDpiAwarenessContext();
                    return CommonUnsafeNativeMethods.TryFindDpiAwarenessContextsEqual(dpiAwareness, DpiAwarenessContext.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Returns a boolean to specify if we should enable all high Dpi improvements.
        /// </summary>
        internal static bool EnableCheckedListBoxHighDpiImprovements
        {
            get
            {
                InitializeDpiHelperForWinforms();
                return enableCheckedListBoxHighDpiImprovements;
            }
        }

        /// <summary>
        /// Returns a boolean to specify if we should enable single-pass scaling of controls.
        /// </summary>
        internal static bool EnableSinglePassScalingOfDpiForms
        {
            get
            {
                InitializeDpiHelperForWinforms();
                return enableSinglePassScalingOfDpiForms;
            }
        }

        /// <summary>
        /// Returns a boolean to specify if we should enable ThreadExceptionDialog HighDPI fixes.
        /// </summary>
        internal static bool EnableThreadExceptionDialogHighDpiImprovements
        {
            get
            {
                InitializeDpiHelperForWinforms();
                return enableThreadExceptionDialogHighDpiImprovements;
            }
        }

        /// <summary>
        /// Returns a boolean to specify if we should enable DataGridViewControl High Dpi improvements
        /// </summary>
        internal static bool EnableDataGridViewControlHighDpiImprovements
        {
            get
            {
                InitializeDpiHelperForWinforms();
                return enableDataGridViewControlHighDpiImprovements;
            }
        }

        /// <summary>
        /// Returns a boolean to specify if we should enable Control Anchor HighDPI fixes
        /// </summary>
        internal static bool EnableAnchorLayoutHighDpiImprovements
        {
            get
            {
                InitializeDpiHelperForWinforms();
                return enableAnchorLayoutHighDpiImprovements;
            }
        }

        /// <summary>
        /// Returns a boolean to specify if we should enable MonthCalendar HighDPI fixes
        /// </summary>
        internal static bool EnableMonthCalendarHighDpiImprovements
        {
            get
            {
                InitializeDpiHelperForWinforms();
                return enableMonthCalendarHighDpiImprovements;
            }
        }

#endif
        internal static int DeviceDpi 
        {
            get 
            {
                Initialize();
                return (int)deviceDpi; 
            }
        }

        private static double LogicalToDeviceUnitsScalingFactor
        {
            get
            {
                if (logicalToDeviceUnitsScalingFactor == 0.0)
                {
                    Initialize();
                    logicalToDeviceUnitsScalingFactor = deviceDpi / LogicalDpi;
                }
                return logicalToDeviceUnitsScalingFactor;
            }
        }

        private static InterpolationMode InterpolationMode
        {
            get 
            {
                if (interpolationMode == InterpolationMode.Invalid) 
                {
                    int dpiScalePercent = (int)Math.Round(LogicalToDeviceUnitsScalingFactor * 100);

                    // We will prefer NearestNeighbor algorithm for 200, 300, 400, etc zoom factors, in which each pixel become a 2x2, 3x3, 4x4, etc rectangle. 
                    // This produces sharp edges in the scaled image and doesn't cause distortions of the original image.
                    // For any other scale factors we will prefer a high quality resizing algorithm. While that introduces fuzziness in the resulting image, 
                    // it will not distort the original (which is extremely important for small zoom factors like 125%, 150%).
                    // We'll use Bicubic in those cases, except on reducing (zoom < 100, which we shouldn't have anyway), in which case Linear produces better 
                    // results because it uses less neighboring pixels.
                    if ((dpiScalePercent % 100) == 0) 
                    {
                        interpolationMode = InterpolationMode.NearestNeighbor;
                    } 
                    else if (dpiScalePercent < 100) 
                    {
                        interpolationMode = InterpolationMode.HighQualityBilinear;
                    } 
                    else 
                    {
                        interpolationMode = InterpolationMode.HighQualityBicubic;
                    }
                }
                return interpolationMode;
            }
        }

        private static Bitmap ScaleBitmapToSize(Bitmap logicalImage, Size deviceImageSize)
        {
            Bitmap deviceImage;
            deviceImage = new Bitmap(deviceImageSize.Width, deviceImageSize.Height, logicalImage.PixelFormat);

            using (Graphics graphics = Graphics.FromImage(deviceImage))
            {
                graphics.InterpolationMode = InterpolationMode;

                RectangleF sourceRect = new RectangleF(0, 0, logicalImage.Size.Width, logicalImage.Size.Height);
                RectangleF destRect = new RectangleF(0, 0, deviceImageSize.Width, deviceImageSize.Height);

                // Specify a source rectangle shifted by half of pixel to account for GDI+ considering the source origin the center of top-left pixel
                // Failing to do so will result in the right and bottom of the bitmap lines being interpolated with the graphics' background color,
                // and will appear black even if we cleared the background with transparent color. 
                // The apparition of these artifacts depends on the interpolation mode, on the dpi scaling factor, etc.
                // E.g. at 150% DPI, Bicubic produces them and NearestNeighbor is fine, but at 200% DPI NearestNeighbor also shows them.
                sourceRect.Offset(-0.5f, -0.5f);

                graphics.DrawImage(logicalImage, destRect, sourceRect, GraphicsUnit.Pixel);
            }

            return deviceImage;
        }

        private static Bitmap CreateScaledBitmap(Bitmap logicalImage, int deviceDpi = 0) 
        {
            Size deviceImageSize = DpiHelper.LogicalToDeviceUnits(logicalImage.Size, deviceDpi);
            return ScaleBitmapToSize(logicalImage, deviceImageSize);
        }

        /// <summary>
        /// Returns whether scaling is required when converting between logical-device units,
        /// if the application opted in the automatic scaling in the .config file.
        /// </summary>
        public static bool IsScalingRequired
        {
            get
            {
                Initialize();
                return deviceDpi != LogicalDpi;
            }
        }

        /// <summary>
        /// Transforms a horizontal or vertical integer coordinate from logical to device units
        /// by scaling it up  for current DPI and rounding to nearest integer value
        /// </summary>
        /// <param name="value">value in logical units</param>
        /// <returns>value in device units</returns>
        public static int LogicalToDeviceUnits(int value, int devicePixels = 0) 
        {
            if (devicePixels == 0) 
            {
                return (int)Math.Round(LogicalToDeviceUnitsScalingFactor * (double)value);
            }
            double scalingFactor = devicePixels / LogicalDpi;
            return (int)Math.Round(scalingFactor * (double)value);
        }

        /// <summary>
        /// Transforms a horizontal or vertical integer coordinate from logical to device units
        /// by scaling it up  for current DPI and rounding to nearest integer value
        /// </summary>
        public static double LogicalToDeviceUnits(double value, int devicePixels = 0)
        {
            if (devicePixels == 0)
            {
                return LogicalToDeviceUnitsScalingFactor * value;
            }
            double scalingFactor = devicePixels / LogicalDpi;
            return scalingFactor * value;
        }

        /// <summary>
        /// Transforms a horizontal integer coordinate from logical to device units
        /// by scaling it up  for current DPI and rounding to nearest integer value
        /// </summary>
        /// <param name="value">The horizontal value in logical units</param>
        /// <returns>The horizontal value in device units</returns>
        public static int LogicalToDeviceUnitsX(int value) 
        {
            return LogicalToDeviceUnits(value, 0);
        }

        /// <summary>
        /// Transforms a vertical integer coordinate from logical to device units
        /// by scaling it up  for current DPI and rounding to nearest integer value
        /// </summary>
        /// <param name="value">The vertical value in logical units</param>
        /// <returns>The vertical value in device units</returns>
        public static int LogicalToDeviceUnitsY(int value) 
        {
            return LogicalToDeviceUnits(value, 0);
        }
        
        /// <summary>
        /// Returns a new Size with the input's
        /// dimensions converted from logical units to device units.
        /// </summary>
        /// <param name="logicalSize">Size in logical units</param>
        /// <returns>Size in device units</returns>
        public static Size LogicalToDeviceUnits(Size logicalSize, int deviceDpi = 0)
        {
            return new Size(LogicalToDeviceUnits(logicalSize.Width, deviceDpi),
                            LogicalToDeviceUnits(logicalSize.Height, deviceDpi));
        }

        /// <summary>
        /// Create and return a new bitmap scaled to the specified size.
        /// </summary>
        /// <param name="logicalImage">The image to scale from logical units to device units</param>
        /// <param name="targetImageSize">The size to scale image to</param>
        public static Bitmap CreateResizedBitmap(Bitmap logicalImage, Size targetImageSize)
        {
            if (logicalImage == null)
            {
                return null;
            }

            return ScaleBitmapToSize(logicalImage, targetImageSize);
        }

        /// <summary>
        /// Create a new bitmap scaled for the device units.
        /// When displayed on the device, the scaled image will have same size as the original image would have when displayed at 96dpi.
        /// </summary>
        /// <param name="logicalBitmap">The image to scale from logical units to device units</param>
        public static void ScaleBitmapLogicalToDevice(ref Bitmap logicalBitmap, int deviceDpi = 0)
        {
            if (logicalBitmap == null) 
            {
                return;
            }
            Bitmap deviceBitmap = CreateScaledBitmap(logicalBitmap, deviceDpi);
            if (deviceBitmap != null)
            {
                logicalBitmap.Dispose();
                logicalBitmap = deviceBitmap;
            }
        }

        /// <summary>
        /// scale logical pixel to the factor
        /// </summary>
        public static int ConvertToGivenDpiPixel(int value, double pixelFactor)
        {
            var scaledValue = (int)Math.Round(value * pixelFactor);
            return scaledValue == 0 ? 1 : scaledValue;
        }

        // This method is used only in System.Design, thus excluding the rest.
        // This is particularly important for System.Drawing, which should not depend 
        // on System.Windows.Forms assembly, where "Button" type is defined. 
#if (!DRAWING_NAMESPACE && !DRAWINGDESIGN_NAMESPACE && !Microsoft_NAMESPACE)
        /// <summary>
        /// Create a new button bitmap scaled for the device units. 
        /// Note: original image might be disposed.
        /// </summary>
        /// <param name="button">button with an image, image size is defined in logical units</param>
        public static void ScaleButtonImageLogicalToDevice(Button button)
        {
            if (button == null)
            {
                return;
            }
            Bitmap buttonBitmap = button.Image as Bitmap;
            if (buttonBitmap == null)
            {
                return;
            }
            Bitmap deviceBitmap = CreateScaledBitmap(buttonBitmap);
            button.Image.Dispose();
            button.Image = deviceBitmap;
        }
#endif

    }

    internal enum DpiAwarenessContext
    {
        DPI_AWARENESS_CONTEXT_UNSPECIFIED = 0,
        DPI_AWARENESS_CONTEXT_UNAWARE = -1,
        DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = -2,
        DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = -3,
        DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
    }
}
