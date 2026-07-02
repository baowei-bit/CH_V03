namespace BW_V01.App.ViewModels;

public abstract class DialogViewModelBase : ViewModelBase
{
    public event EventHandler<bool?>? RequestClose;

    protected void Close(bool? dialogResult)
    {
        RequestClose?.Invoke(this, dialogResult);
    }
}
