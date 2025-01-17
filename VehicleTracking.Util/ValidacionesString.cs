using System.Text.RegularExpressions;

namespace VehicleTracking.Util
{
    public static class ValidacionesString
    {

        public static bool StringEsIP(string value)
        {
            try
            {
                if (!string.IsNullOrEmpty(value))
                {
                    if (value!.ToString() == "::1")
                    {
                        return true;
                    }
                    if (!Regex.IsMatch(value.ToString()!, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$"))
                    {
                        return false;
                    }
                    string[] nums_ip = value.ToString()!.Split('.');
                    foreach (string num in nums_ip)
                    {
                        int number;
                        if (!int.TryParse(num, out number) || number < 0 || number > 255)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
