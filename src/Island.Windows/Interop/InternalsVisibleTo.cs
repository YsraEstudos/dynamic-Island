using System.Runtime.CompilerServices;

// Lets the test project reach internal helpers (ClipboardImageConverter, NativeMethods).
[assembly: InternalsVisibleTo("Island.Windows.Tests")]
