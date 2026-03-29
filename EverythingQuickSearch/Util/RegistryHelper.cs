using Microsoft.Win32;
using System.Diagnostics;

namespace EverythingQuickSearch.Util
{
public class RegistryHelper
{
    private readonly string _regKeyName;
    public RegistryHelper(string regkeyname)
    {
        this._regKeyName = regkeyname;
    }
   public void WriteToRegistryRoot(string keyName, object value)
    {
        if (value is bool b) value = b ? 1 : 0;
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey($"SOFTWARE\\{_regKeyName}"))
            {
                key.SetValue(keyName, value);
            }
        }
        catch { }
    }

    public bool KeyExistsRoot(string keyName)
    {
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
    public object ReadKeyValueRoot(string keyName)
    {
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
                            return (string)value;
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
    public object ReadKeyValueRootInt(string keyName)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey($"SOFTWARE\\{_regKeyName}")!)
            {
                if (key != null)
                {
                    return Int32.Parse(key.GetValue(keyName).ToString());
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
