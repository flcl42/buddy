namespace Buddy.App.Controls;

/// <summary>
/// An entry whose native Windows chrome is styled by <c>FramedInputChrome</c>.
/// Keeping this as a distinct control avoids changing editors elsewhere in the app.
/// </summary>
public sealed class FramedEntry : Entry
{
}

/// <summary>
/// An entry whose visual frame is supplied by a parent composite control.
/// </summary>
public sealed class BorderlessEntry : Entry
{
}

/// <summary>
/// A picker whose native Windows chrome is styled by <c>FramedInputChrome</c>.
/// </summary>
public sealed class FramedPicker : Picker
{
}

/// <summary>
/// A multiline editor with the same rounded native chrome as the other inputs.
/// </summary>
public sealed class FramedEditor : Editor
{
}

/// <summary>
/// A multiline editor whose visual frame is supplied by a parent border.
/// </summary>
public sealed class BorderlessEditor : Editor
{
}
