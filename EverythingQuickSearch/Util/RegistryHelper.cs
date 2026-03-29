using Microsoft.Win32;
using System.Diagnostics;

namespace EverythingQuickSearch.Util
{
public class RegistryHelper
{
    private readonly string _regKeyName;
    public RegistryHelper(string regkeyname)
    {
        this._regKeyName = SanitizeKeyName(regkeyname);
    }

    /// <summary>
    /// Strips backslashes and forward slashes from a registry key name to prevent
    /// path injection attacks that could write to unintended registry subkeys.
    /// </summary>
    private static string SanitizeKeyName(string name) =>
        name.Replace(@"\", "").Replace("/", "");

   public void WriteToRegistryRoot(string keyName, object value)
    {
        keyName = SanitizeKeyName(keyName);
        if (value is bool b) value = b ? 1 : 0;
        else if (value is double d) value = d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            using (RegistryKey? key = Registry.CurrentUser.CreateSubKey($"SOFTWARE\\{_regKeyName}"))
            {
                key?.SetValue(keyName, value);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error WriteToRegistryRoot '{keyName}': {ex.Message}");
        }
    }

    public bool KeyExistsRoot(string keyName)
    {
        keyName = SanitizeKeyName(keyName);
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey($"SOFTWARE\\{_regKeyName}")!)
            {
                if (key != null)
                {
                    if (key.GetValue(keyName) != null)
                    {
                        Debug.WriteLine($"exists: {keyName},{key.GetValue(keyName)}");

                        return true;
                    }
                }
            }
            Debug.WriteLine($"doesnt exist: {keyName}");
            return false;
        }
        catch
        {
            Debug.WriteLine($"error opening HKCU\\SOFTWARE\\{_regKeyName}, {keyName}");
            return false;
        }
    }
    public object? ReadKeyValueRoot(string keyName)
    {
        keyName = SanitizeKeyName(keyName);
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey($"SOFTWARE\\{_regKeyName}")!)
            {
                if (key != null)
                {
                    object? value = key.GetValue(keyName);
                    if (value != null)
                    {
                        if (value is bool)
                        {
                            Debug.WriteLine($"returned bool for {keyName}");
                            return (bool)value;
                        }
                        else if (value is int intVal)
                        {
                            Debug.WriteLine($"returned int/bool for {keyName}");
                            return intVal != 0;
                        }
                        else if (bool.TryParse(value.ToString(), out bool boolValue))
                        {
                            Debug.WriteLine($"returned Parsed bool for {keyName}");
                            return boolValue;
                        }
                        else
                        {
                            Debug.WriteLine($"returned string for {keyName}");
                            return value.ToString() ?? string.Empty;
                        }

                    }
                }
            }
            Debug.WriteLine($"couldn't return for {keyName}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Error ReadKeyValueRoot: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads a boolean registry value, normalising int (0/1), bool, and string ("True"/"False")
    /// representations to a plain <see langword="bool"/>. Returns <see langword="false"/> if the
    /// key does not exist or cannot be parsed.
    /// </summary>
    public bool ReadKeyValueRootBool(string keyName)
    {
        keyName = SanitizeKeyName(keyName);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($"SOFTWARE\\{_regKeyName}");
            var val = key?.GetValue(keyName);
            return val switch
            {
                bool bv => bv,
                int iv => iv != 0,
                string sv => bool.TryParse(sv, out var bv2) && bv2,
                _ => false
            };
        }
        catch { return false; }
    }

    public int? ReadKeyValueRootInt(string keyName)
    {
        keyName = SanitizeKeyName(keyName);
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey($"SOFTWARE\\{_regKeyName}")!)
            {
                if (key != null)
                {
                    var value = key.GetValue(keyName);
                    if (value != null && int.TryParse(value.ToString(), out int intResult))
                        return intResult;
                }
            }

            Debug.WriteLine($"couldn't return value for {keyName}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Error ReadKeyValueRootInt: " + ex.Message);
            return null;
        }
    }

    public double? ReadKeyValueRootDouble(string keyName)
    {
        keyName = SanitizeKeyName(keyName);
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey($"SOFTWARE\\{_regKeyName}")!)
            {
                if (key != null)
                {
                    var value = key.GetValue(keyName);
                    if (value != null && double.TryParse(value.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d))
                        return d;
                }
            }
            Debug.WriteLine($"couldn't return double value for {keyName}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Error ReadKeyValueRootDouble: " + ex.Message);
            return null;
        }
    }

    public void AddToAutoRun(string appName, string appPath)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true)!)
            {
                key.SetValue(appName, appPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Error adding to autorun: " + ex.Message);
        }
    }

    public void RemoveFromAutoRun(string appName)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true)!)
            {
                key.DeleteValue(appName, false);
            }

        }
        catch (Exception ex)
        {
            Debug.WriteLine("Error removing from autorun: " + ex.Message);
        }
    }



}
}
