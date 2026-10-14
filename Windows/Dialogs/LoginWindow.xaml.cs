using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace AudioRecorderTrayApp
{
    public partial class LoginWindow : Window
    {
        private enum AuthMode
        {
            Login,
            Register
        }

        private AuthMode _currentMode = AuthMode.Login;
        public LoginWindow()
        {
            InitializeComponent();
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlock.Text = "";
            ActionButton.IsEnabled = false;
            try
            {
                Debug.WriteLine("ActionButton_Click: Начинаю операцию входа/регистрации.");
                if (_currentMode == AuthMode.Login)
                {
                    await SignInUser();
                }
                else
                {
                    await SignUpUser();
                }

                Debug.WriteLine("ActionButton_Click: Операция завершена успешно.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"!!! КРИТИЧЕСКАЯ ОШИБКА ПОЙМАНА В ActionButton_Click: {ex.ToString()}");
                try
                {
                    ErrorTextBlock.Text = FormatErrorMessage(ex.Message);
                }
                catch (Exception formatEx)
                {
                    ErrorTextBlock.Text = $"Внутренняя ошибка обработчика: {formatEx.Message}";
                }
            }
            finally
            {
                ActionButton.IsEnabled = true;
                Debug.WriteLine("ActionButton_Click: Блок finally выполнен.");
            }
        }

        private async Task SignInUser()
        {
            Debug.WriteLine("SignInUser: Вызываю Supabase.Auth.SignIn...");
            var session = await App.Supabase.Auth.SignIn(EmailTextBox.Text, PasswordBox.Password);
            Debug.WriteLine("SignInUser: Ответ от Supabase получен.");
            if (session != null)
            {
                await App.SaveSession(session);
                this.DialogResult = true;
                this.Close();
            }
        }

        private async Task SignUpUser()
        {
            Debug.WriteLine("SignUpUser: Вызываю Supabase.Auth.SignUp...");
            var session = await App.Supabase.Auth.SignUp(EmailTextBox.Text, PasswordBox.Password);
            Debug.WriteLine("SignUpUser: Ответ от Supabase получен.");
            if (session != null)
            {
                System.Windows.MessageBox.Show("Регистрация успешна! Пожалуйста, подтвердите ваш email и войдите в аккаунт.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                SwitchToLoginMode();
            }
        }

        private string FormatErrorMessage(string? message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "Произошла неизвестная ошибка.";
            }

            if (message.ToLower().Contains("invalid login credentials"))
                return "Неверный email или пароль.";
            if (message.ToLower().Contains("user already registered"))
                return "Пользователь с таким email уже существует.";
            if (message.ToLower().Contains("password should be at least 6 characters"))
                return "Пароль должен содержать не менее 6 символов.";
            return $"Неизвестная ошибка: {message}";
        }

        private void SwitchModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == AuthMode.Login)
                SwitchToRegisterMode();
            else
                SwitchToLoginMode();
        }

        private void SwitchToLoginMode()
        {
            _currentMode = AuthMode.Login;
            TitleTextBlock.Text = "Вход в аккаунт";
            ActionButton.Content = "Войти";
            SwitchModePromptTextBlock.Text = "Нет аккаунта?";
            SwitchModeButton.Content = "Зарегистрироваться";
            ErrorTextBlock.Text = "";
        }

        private void SwitchToRegisterMode()
        {
            _currentMode = AuthMode.Register;
            TitleTextBlock.Text = "Создание аккаунта";
            ActionButton.Content = "Создать аккаунт";
            SwitchModePromptTextBlock.Text = "Уже есть аккаунт?";
            SwitchModeButton.Content = "Войти";
            ErrorTextBlock.Text = "";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
    }
}
