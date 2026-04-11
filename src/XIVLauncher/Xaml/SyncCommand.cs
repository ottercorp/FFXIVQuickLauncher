using System;
using System.Windows.Input;

namespace XIVLauncher.Xaml
{
    public class SyncCommand : ICommand
    {
        private readonly Action<object> _command;
        private readonly Func<bool> _canExecute;

        public SyncCommand(Action<object> command)
        {
            _command = command;
            _canExecute = () => true;
        }

        public SyncCommand(Action<object> command, Func<bool> canExecute)
        {
            _command = command;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute();
        }

        public void Execute(object parameter)
        {
            _command(parameter);
        }
    }
}
