using GooglePal.Pages;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Devices.Printers;

namespace GooglePal
{
    internal partial class Chrome
    {
        /// <summary>
        /// The JSON object that chrome stores for each profile in local state.
        /// </summary>
        public record ChromeProfile
        {
            public required double  active_time;
            public required string  avatar_icon;
            public required bool    background_apps;
            public required int     default_avatar_fill_color;
            public required int     default_avatar_stroke_color;
            public required string  enterprise_label;
            public required bool    force_signin_profile_locked;
            public required string  gaia_given_name;
            public required string  gaia_id;
            public required string  gaia_name;
            public required string  gaia_picture_file_name;
            public required string  hosted_domain;
            public required bool    is_consented_primary_account;
            public required bool    is_ephemeral;
            public required bool    is_glic_eligible;
            public required int     is_managed;
            public required bool    is_using_default_avatar;
            public required bool    is_using_default_name;
            public required bool    is_using_new_placeholder_avatar_icon;
            public required string  last_downloaded_gaia_picture_url_with_size;
            public required string  managed_user_id;
            public required int     metrics_bucket_index;
            public required string  name;
            public required int     profile_color_seed;
            public required int     profile_highlight_color;
            public required string  shortcut_name;
            public required bool    signin_with_credential_provider;
            public required bool    user_accepted_account_management;
            public required string  user_name;
        }

        public class Profile
        {
            public required string Name { get; set; }
            public required string Folder { get; set; }
            public required string ProfilePicturePath { get; set; }
            public required string TaskbarIconPath { get; set; }

            //region constructor chain

            public static Profile ParseChromeProfile(ChromeProfile profile, string profileFolder)
            {
                string profileRoot = Path.Combine(CHROME_USER_DATA_FOLDER, profileFolder);

                string iconPath = Path.Combine(profileRoot, profile.gaia_picture_file_name);


                // default pfp finding and getting
                if (string.IsNullOrEmpty(profile.gaia_picture_file_name))
                {
                    string? path = SaveDefaultProfileIcon(profile);
                    if (path != null)
                        iconPath = path;
                }

                // TODO: circularize the pfp

                string taskbarIconPath = Path.Combine(profileRoot, "Google Profile.ico");

                return new() {
                    Name                = profile.name,
                    Folder              = profileFolder,
                    ProfilePicturePath  = iconPath,
                    TaskbarIconPath     = taskbarIconPath
                };
            }


            public static Profile? ParseChromeProfileJSON(JsonProperty FullJSONProperty)
            {
                // get profile directory
                string directoryName = FullJSONProperty.Name;

                // get the raw JSON
                // TODO: make this not... like this
                string json = FullJSONProperty.Value.ToString();
                ChromeProfile? rawProfile = JsonConvert.DeserializeObject<ChromeProfile>(json, JSON_SETTINGS);

                if (rawProfile != null)
                    return ParseChromeProfile(rawProfile, directoryName);
                return null;
            }

            //endregion constructor chain

            public static string? SaveDefaultProfileIcon(ChromeProfile profile)
            {
                string path = Path.Combine(SettingsManager.SETTINGS_CONFIG_DIRECTORY, "ProfileImgs", profile.name + ".png");
                string folder = Path.Combine(SettingsManager.SETTINGS_CONFIG_DIRECTORY, "ProfileImgs");

                Image? defaultPFP = PFPIcon.CHROME_DEFAULT_PFP;

                if (defaultPFP == null)
                    return null;

                Image pfpImg = CreateTintedProfileIcon(defaultPFP, Color.FromArgb(profile.default_avatar_fill_color));
                //pfpImg = PFPIcon.Circularize(pfpImg);

                if (!Path.Exists(folder))
                    Directory.CreateDirectory(folder);

                try
                {
                    Debug.WriteLine("Saved PFP to: " + path);
                    pfpImg.Save(path);
                    return path;
                }
                catch (Exception e)
                {
                    Debug.WriteLine("ERROR WHILE SAVING ICON: " + e);
                    return null;
                }
            }

            public static Bitmap CreateTintedProfileIcon(Image baseSilhouette, Color tintColor, int size = 64)
            {
                Bitmap canvas = new Bitmap(size, size);
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    // draw colored circular background
                    using (Brush brush = new SolidBrush(tintColor))
                    {
                        g.Clear(tintColor);
                        g.FillEllipse(brush, 0, 0, size, size);
                    }

                    // overlay the base profile silhouette
                    g.DrawImage(baseSilhouette, new Rectangle(0, 0, size, size));
                }
                return canvas;
            }


            // override equality check for things like FirstOrDefault
            public static bool operator ==(Profile? a, Profile? b) => a?.Equals(b) == true;
            public static bool operator !=(Profile? a, Profile? b) => !a?.Equals(b) == true;
            public override bool Equals(object? obj) => obj != null && Equals(obj as Profile);
            public bool Equals(Profile? other) => !object.ReferenceEquals(other, null) && Folder == other.Folder;
            public override int GetHashCode() => Folder.GetHashCode();
        }

        public class PFPIcon
        {
            // pfp default
            //public static readonly string CHROME_DEFAULT_PFP = "Assets\\ChromeDefaultPFP.png";
            public static readonly string CHROME_DEFAULT_PFP_RES_PATH = "GooglePal.Assets.ChromeDefaultPFP.png";
            public static readonly Image? CHROME_DEFAULT_PFP = GetResourceImage(CHROME_DEFAULT_PFP_RES_PATH);

            // clipping mask
            //public static readonly string CIRCLE_IMAGE_MASK = "Assets\\ChromePFPMask.png";
            public static readonly string CHROME_PFP_MASK_RES_PATH = "GooglePal.Assets.ChromePFPMask.png";
            public static readonly Image? CHROME_PFP_MASK = GetResourceImage(CHROME_PFP_MASK_RES_PATH);

            public static Image? GetResourceImage(string resourcePath)
            {
                Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath);

                if (stream == null) return null;

                return Bitmap.FromStream(stream);
            }

            //// https://stackoverflow.com/a/15449620
            //public static Bitmap GetImageByName(string imageName)
            //{
            //    System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
            //    string resourceName = asm.GetName().Name + ".Properties.Resources";
            //    var rm = new System.Resources.ResourceManager(resourceName, asm);
            //    return (Bitmap)rm.GetObject(imageName);

            //}


            public static Image Circularize(Image image)
            {
                //TODO: make it work
                SizeF size = image.PhysicalDimension;

                Graphics imgGraphic = Graphics.FromImage(image);

                // https://stackoverflow.com/a/28517165
                GraphicsPath clippingRegion = new GraphicsPath();

                clippingRegion.AddEllipse(
                    image.Width / 2,
                    image.Height / 2,
                    image.Width,
                    image.Height
                );

                Region clipRegion = new Region(clippingRegion);

                // actually do the clipping
                imgGraphic.ExcludeClip(clipRegion);
                imgGraphic.Clear(Color.Transparent);
                imgGraphic.ResetClip();

                return new Bitmap(image.Width, image.Height, imgGraphic);
                
            }
        }
    }
}