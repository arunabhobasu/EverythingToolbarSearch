using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EverythingQuickSearch
{
    /// <summary>
    /// Represents a high-level file category used for search filtering.
    /// </summary>
    public enum Category
    {
        Image,
        Document,
        Audio,
        Video,
        Executable,
        Compressed,
        File,
        Folder,
        All
    }

    /// <summary>
    /// Builds Everything SDK search prefix strings for a given <see cref="Category"/>.
    /// </summary>
    public class SearchCategory
    {


        private static readonly Dictionary<Category, string[]> CategoryExtensions = new()
        {
            { Category.Image, FileTypes.Image },
            { Category.Document, FileTypes.Document },
            { Category.Audio, FileTypes.Audio },
            { Category.Video, FileTypes.Video },
            { Category.Executable, FileTypes.Executable },
            { Category.Compressed, FileTypes.Compressed },
            { Category.File, Array.Empty<string>() },
            { Category.Folder, Array.Empty<string>() },
            { Category.All, Array.Empty<string>() }
        };

        /// <summary>
        /// Returns the Everything SDK search prefix for the given category.
        /// For example, <see cref="Category.Image"/> returns <c>"ext:jpg;png;... "</c>.
        /// </summary>
        public static string GetExtensions(Category category)
        {
            if (!CategoryExtensions.TryGetValue(category, out var exts))
            {
                return string.Empty;
            }
            if (category == Category.File)
            {
                return "file: ";
            }
            if (category == Category.Folder)
            {
                return "folder: ";
            }
            if (category == Category.All)
            {
                return string.Empty;
            }
            return "ext:" + string.Join(";", exts) + " ";
        }
    }

    public static class FileTypes
    {
        public static readonly string[] Image =
        {
            "3dm","3ds","max","bmp","dds","gif","jpg","jpeg","png","psd","xcf",
            "tga","thm","tif","tiff","yuv","ai","eps","ps","svg","dwg","dxf",
            "gpx","kml","kmz","heic","webp","ani","ico","avif","jfif","jxr",
            "hdr","exr","ppm","pgm","pbm","pcd","bmpf","heif"
        };

        public static readonly string[] Document =
        {
            "doc","docx","ebook","log","nb","md","msg","odt","org","pages",
            "pdf","rtf","rst","tex","txt","wpd","wps","xls","xlsx","ppt",
            "odp","ods","csv","ics","vcf","c","chm","cpp","cxx","docm","dot",
            "dotm","dotx","h","hpp","htm", "html","hxx","ini","java","lua","mht",
            "mhtml","potx","potm","ppam","ppsm","ppsx","pps","pptm","rtf","sldm",
            "sldx","thmx","vsd","wri","xlam","xlsb","xlsm","xltm","xltx","xml"
        };

        public static readonly string[] Audio =
        {
            "aac","aiff","ape","au","flac","gsm","it","m3u","m4a","mid",
            "mod","mp3","mpa","ogg","pls","ra","s3m","sid","wav","wma","xm",
            "ac3","aif","aifc","cda","dts","fla","m1a","m2a","rmi","snd",
            "umx"
        };

        public static readonly string[] Video =
        {
            "3g2","3gp","3gp2","3gpp","aaf","amr","amv","asf","avchd","avi",
            "bdmv","bik","d2v","divx","drc","dsa","dsm","dss","dsv","evo",
            "flv","f4v","flc","fli","flic","hdmov","ifo","ivf","m1v","m2p",
            "m2t","m2ts","m2v","m4b","m4p","m4v","mkv","mp2v","mp4","mp4v",
            "mpe","mpeg","mpg","mpls","mpv2","mpv4","mov","mts","ogm","ogv",
            "pss","pva","qt","ram","ratdvd","rm","rmm","rmvb","roq","rpm",
            "smil","smk","swf","tp","tpr","ts","vob","vp6","webm","wm","wmp",
            "wmv",
        };

        public static readonly string[] Executable =
        {
            "exe", "msi", "bat", "cmd", "com", "msp", "scr",
            "ps1", "sh", "bash", "csh", "ksh", "zsh",
            "py", "rb", "pl", "vbs", "wsf", "psc1", "psm1"
        };

        public static readonly string[] Compressed =
        {
            "7z","a","apk","ar","bz2","cab","cpio","deb","dmg","gz",
            "gzip","iso","jar","lha","mar","pea","rar","rpm","s7z","shar",
            "tar","tbz2","tgz","whl","xpi","zip","zipx","xz",
            "pak","bin",
        };
        public static readonly string[] All = Array.Empty<string>();
        public static readonly string[] File = Array.Empty<string>();
        public static readonly string[] Folder = Array.Empty<string>();
    }

}