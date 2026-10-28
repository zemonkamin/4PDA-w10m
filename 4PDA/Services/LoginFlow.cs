using System;

namespace _4PDA.Services
{
    internal enum LoginStep { Credentials, Captcha, Complete }

    // Credentials live only for this attempt and are cleared when leaving the page.
    internal sealed class LoginFlow
    {
        internal LoginStep Step { get; private set; }
        internal string Login { get; private set; }
        internal string Password { get; private set; }
        internal bool Hidden { get; private set; }
        internal string CaptchaUrl { get; private set; }

        internal LoginFlow() { Reset(); }
        internal void Begin(string login, string password, bool hidden)
        {
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
                throw new ArgumentException("Введите логин и пароль.");
            Login = login.Trim(); Password = password; Hidden = hidden;
            CaptchaUrl = string.Empty; Step = LoginStep.Credentials;
        }
        internal void Challenge(string url)
        {
            if (string.IsNullOrEmpty(Password) || string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Проверка входа недоступна. Введите данные ещё раз.");
            CaptchaUrl = url; Step = LoginStep.Captcha;
        }
        internal void Reset()
        {
            Login = Password = CaptchaUrl = string.Empty;
            Hidden = false; Step = LoginStep.Credentials;
        }
        internal void Complete() { Reset(); Step = LoginStep.Complete; }
    }
}
