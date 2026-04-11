namespace XIVLauncher.Xaml
{
    public enum MessageBoxButton
    {
        OK,
        OKCancel,
        YesNoCancel,
        YesNo,
    }

    public enum MessageBoxResult
    {
        None,
        OK,
        Cancel,
        Yes,
        No,
    }

    public enum MessageBoxImage
    {
        None,
        Hand,
        Question,
        Exclamation,
        Asterisk,
        Error = Hand,
        Warning = Exclamation,
        Information = Asterisk,
    }
}
