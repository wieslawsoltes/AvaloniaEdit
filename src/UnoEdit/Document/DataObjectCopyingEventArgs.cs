using Avalonia.Input;
using Avalonia.Interactivity;
using UnoEdit.Utils;

namespace UnoEdit.Document;

public class DataObjectCopyingEventArgs : RoutedEventArgs
{
    public bool CommandCancelled { get; private set; }
    public IDataTransfer DataObject { get; private set; }
    public bool IsDragDrop { get; private set; }

    public DataObjectCopyingEventArgs(IDataTransfer dataObject, bool isDragDrop) :
        base(DataObjectEx.DataObjectCopyingEvent)
    {
        DataObject = dataObject;
        IsDragDrop = isDragDrop;
    }

    public void CancelCommand() => CommandCancelled = true;
}