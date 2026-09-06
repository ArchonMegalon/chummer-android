namespace Android.App
{
    public class Activity
    {
        public Android.Content.ContentResolver? ContentResolver { get; init; }

        public void StartActivityForResult(Android.Content.Intent intent, int requestCode)
        {
        }
    }
}

namespace Android.Content
{
    [Flags]
    public enum ActivityFlags
    {
        GrantReadUriPermission = 1,
        GrantWriteUriPermission = 2,
        GrantPersistableUriPermission = 4
    }

    public sealed class Intent
    {
        public const string ActionOpenDocument = "android.intent.action.OPEN_DOCUMENT";
        public const string ActionCreateDocument = "android.intent.action.CREATE_DOCUMENT";
        public const string CategoryOpenable = "android.intent.category.OPENABLE";
        public const string ExtraMimeTypes = "android.intent.extra.MIME_TYPES";
        public const string ExtraTitle = "android.intent.extra.TITLE";

        public Intent(string action)
        {
        }

        public void AddCategory(string category)
        {
        }

        public void SetType(string mediaType)
        {
        }

        public void AddFlags(ActivityFlags flags)
        {
        }

        public void PutExtra(string name, string value)
        {
        }

        public void PutExtra(string name, string[] value)
        {
        }
    }

    public class ContentResolver
    {
        public Stream? OpenInputStream(Android.Net.Uri uri) => null;

        public Stream? OpenOutputStream(Android.Net.Uri uri, string mode) => null;

        public Android.Database.ICursor? Query(
            Android.Net.Uri uri,
            string[] projection,
            string? selection,
            string[]? selectionArgs,
            string? sortOrder) => null;

        public string? GetType(Android.Net.Uri uri) => null;

        public void TakePersistableUriPermission(Android.Net.Uri uri, ActivityFlags flags)
        {
        }
    }
}

namespace Android.Database
{
    public interface ICursor : IDisposable
    {
        bool MoveToFirst();
        int GetColumnIndex(string columnName);
        string? GetString(int columnIndex);
        long GetLong(int columnIndex);
        bool IsNull(int columnIndex);
    }
}

namespace Android.Graphics
{
    public sealed class Bitmap : IDisposable
    {
        public enum Config
        {
            Argb8888
        }

        public int Width => 1;
        public int Height => 1;
        public bool IsPremultiplied => true;

        public Config? GetConfig() => Config.Argb8888;

        public void Dispose()
        {
        }
    }

    public static class BitmapFactory
    {
        public sealed class Options : IDisposable
        {
            public bool InJustDecodeBounds { get; init; }
            public Bitmap.Config? InPreferredConfig { get; init; }
            public bool InPremultiplied { get; init; }
            public int OutWidth => 1;
            public int OutHeight => 1;
            public string? OutMimeType => "image/png";

            public void Dispose()
            {
            }
        }

        public static Bitmap? DecodeByteArray(byte[] data, int offset, int length, Options options)
            => new();
    }
}

namespace Android.Net
{
    public class Uri
    {
        public string? Scheme => "content";

        public override string ToString() => "content://document";
    }
}

namespace Android.Provider
{
    public static class IOpenableColumns
    {
        public const string DisplayName = "_display_name";
        public const string Size = "_size";
    }
}

namespace Java.Lang
{
    public class SecurityException : Exception
    {
    }
}

namespace Microsoft.Maui.ApplicationModel
{
    public static class Platform
    {
        public static Android.App.Activity? CurrentActivity { get; set; }
    }
}
