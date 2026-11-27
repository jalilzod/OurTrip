using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// UI language: English or Russian, chosen in the start menu (or in the Wi-Fi panel in the game).
/// Saved on the phone. First start: Russian if the phone is in Russian, otherwise English.
/// Usage: Loc.T("Create a trip") or Loc.F("Lanterns  {0} / {1}", lit, total).
/// To add a language later: add a dictionary and a value to Lang.
/// </summary>
public static class Loc
{
    public enum Lang { EN = 0, RU = 1 }

    const string Key = "lang";
    static Lang? current;

    public static Lang Current
    {
        get
        {
            if (current == null)
            {
                int def = Application.systemLanguage == SystemLanguage.Russian ? 1 : 0;
                current = (Lang)PlayerPrefs.GetInt(Key, def);
            }
            return current.Value;
        }
        set
        {
            current = value;
            PlayerPrefs.SetInt(Key, (int)value);
            PlayerPrefs.Save();
            culture = null;
        }
    }

    public static bool IsRussian => Current == Lang.RU;

    static CultureInfo culture;
    /// <summary>For dates: "27 November 2026" / "27 ноября 2026".</summary>
    public static CultureInfo Culture
    {
        get
        {
            if (culture != null) return culture;
            try { culture = IsRussian ? new CultureInfo("ru-RU") : CultureInfo.InvariantCulture; }
            catch { culture = CultureInfo.InvariantCulture; }
            return culture;
        }
    }

    public static string T(string en)
    {
        if (string.IsNullOrEmpty(en) || Current == Lang.EN) return en;
        return Ru.TryGetValue(en, out var r) ? r : en;
    }

    public static string F(string en, params object[] args) => string.Format(T(en), args);

    static readonly Dictionary<string, string> Ru = new Dictionary<string, string>
    {
        // Start menu + connection
        { "Starting...", "Запуск..." },
        { "Signing in...", "Вход..." },
        { "Ready", "Готово" },
        { "Sign-in failed: {0}", "Не удалось войти: {0}" },
        { "Creating trip...", "Создаём поездку..." },
        { "Hosting ({0})", "Поездка создана ({0})" },
        { "Could not create the trip", "Не удалось создать поездку" },
        { "Host failed: {0}", "Ошибка создания: {0}" },
        { "Joining {0}...", "Подключаемся к {0}..." },
        { "Connecting ({0})...", "Соединяемся ({0})..." },
        { "Could not connect", "Не удалось подключиться" },
        { "Join failed: {0}", "Ошибка подключения: {0}" },
        { "Partner left", "Партнёр вышел" },
        { "Partner joined", "Партнёр присоединился" },
        { "Connected ({0})", "Подключено ({0})" },
        { "Disconnected", "Отключено" },
        { "Disconnected: {0}", "Отключено: {0}" },
        { "I AM", "Я" },
        { "Boy", "Парень" },
        { "Girl", "Девушка" },
        { "Create a trip", "Создать поездку" },
        { "OR JOIN WITH A CODE", "ИЛИ ВОЙТИ ПО КОДУ" },
        { "Join", "Войти" },
        { "Connection: {0} (tap to change)", "Соединение: {0} (нажми, чтобы сменить)" },
        { "LANGUAGE", "ЯЗЫК" },

        // Session panel
        { "TRIP CODE", "КОД ПОЕЗДКИ" },
        { "Code copied. Send it to your love", "Код скопирован. Отправь его своей половинке" },
        { "Players {0}/2   ·   Ping {1}", "Игроки {0}/2   ·   Пинг {1}" },
        { "waiting for partner", "ждём партнёра" },
        { "Leave trip", "Выйти из поездки" },

        // Hug / kiss / sit
        { "A hug for you?", "Обнимемся?" },
        { "A kiss on the cheek?", "Поцелуй в щёчку?" },
        { "Yes", "Да" },
        { "Not now", "Не сейчас" },
        { "Waiting for an answer...", "Ждём ответа..." },
        { "Sit together", "Посидеть вместе" },
        { "Stand up", "Встать" },

        // World + music + cat + photo
        { "TIME OF DAY", "ВРЕМЯ СУТОК" },
        { "WEATHER", "ПОГОДА" },
        { "Rain is on", "Идёт дождь" },
        { "Make it rain", "Включить дождь" },
        { "NOW PLAYING  ·  FOR BOTH OF US", "СЕЙЧАС ИГРАЕТ  ·  ДЛЯ НАС ОБОИХ" },
        { "Nothing playing", "Музыка выключена" },
        { "Volume is only for your phone", "Громкость только на твоём телефоне" },
        { "Here, kitty kitty!", "Кис-кис-кис!" },
        { "Saved to Photos", "Сохранено в Фото" },
        { "Photo could not be saved", "Не удалось сохранить фото" },

        // Fireflies + lanterns
        { "You caught the fireflies together!", "Вы поймали светлячков вместе!" },
        { "Fireflies caught: {0}  <3", "Поймано светлячков: {0}  <3" },
        { "Fireflies  {0} / {1}", "Светлячки  {0} / {1}" },
        { "All lanterns lit  <3", "Все фонарики горят  <3" },
        { "Lanterns  {0} / {1}", "Фонарики  {0} / {1}" },
        { "Light the lantern", "Зажечь фонарик" },

        // Our tree
        { "Our tree grew!", "Наше дерево выросло!" },
        { "We planted our seed!", "Мы посадили семечко!" },
        { "Watered! Waiting for your partner to water too.", "Полито! Ждём, пока партнёр тоже польёт." },
        { "A perfect spot for our tree", "Идеальное место для нашего дерева" },
        { "{0}  ·  grown {1} days", "{0}  ·  растёт {1} дн." },
        { "Our tree", "Наше дерево" },
        { "Plant our seed", "Посадить семечко" },
        { "Watered together today  <3", "Сегодня полили вместе  <3" },
        { "Watered, waiting for you two", "Полито, ждём второго" },
        { "Water our tree", "Полить наше дерево" },
        { "Seed", "Семечко" },
        { "Sprout", "Росток" },
        { "Little tree", "Маленькое деревце" },
        { "Young tree", "Молодое дерево" },
        { "Big tree", "Большое дерево" },
        { "Blossoming tree", "Цветущее дерево" },

        // Letters
        { "A letter is waiting for you somewhere...", "Где-то тебя ждёт письмо..." },
        { "Your note is waiting here for {0}", "Записка оставлена здесь. Её найдёт {0}" },
        { "A note for {0}", "Записка · Кому: {0}" },
        { "From {0}", "От: {0}" },
        { "To {0}", "Кому: {0}" },
        { "Leave it here", "Оставить здесь" },
        { "Our letters", "Наши письма" },
        { "read", "прочитано" },
        { "not read yet", "ещё не прочитано" },
        { "NEW, find it!", "НОВОЕ, найди его!" },
        { "No letters yet. Write the first one!", "Писем пока нет. Напиши первое!" },
        { "Open the letter", "Открыть письмо" },
    };
}