namespace CH_V03.App.ViewModels;

public abstract class DialogViewModelBase : ViewModelBase
{
    public event EventHandler<bool?>? RequestClose;

    protected void Close(bool? dialogResult)
    {
        RequestClose?.Invoke(this, dialogResult);
    }
}
