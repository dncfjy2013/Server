using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common.Helpers
{
    public static class DateTimeHelper
    {
        // 计算两个日期之间的天数差
        public static int DaysBetween(DateTime startDate, DateTime endDate)
        {
            return (endDate - startDate).Days;
        }

        // 获取指定日期是星期几的字符串表示
        public static string GetDayOfWeek(DateTime date)
        {
            return date.DayOfWeek.ToString();
        }

        // 将日期时间转换为指定格式的字符串
        public static string ToCustomFormat(DateTime date, string format)
        {
            return date.ToString(format);
        }

        // 判断指定日期是否为闰年
        public static bool IsLeapYear(DateTime date)
        {
            return DateTime.IsLeapYear(date.Year);
        }

        // 获取指定日期所在月份的第一天
        public static DateTime GetFirstDayOfMonth(DateTime date)
        {
            return new DateTime(date.Year, date.Month, 1);
        }

        // 获取指定日期所在月份的最后一天
        public static DateTime GetLastDayOfMonth(DateTime date)
        {
            return new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
        }

        // 计算两个日期之间的周数差
        public static int WeeksBetween(DateTime startDate, DateTime endDate)
        {
            return (int)Math.Floor((endDate - startDate).TotalDays / 7);
        }

        // 计算两个日期之间的月数差
        public static int MonthsBetween(DateTime startDate, DateTime endDate)
        {
            return (endDate.Year - startDate.Year) * 12 + endDate.Month - startDate.Month;
        }

        // 计算两个日期之间的年数差
        public static int YearsBetween(DateTime startDate, DateTime endDate)
        {
            int years = endDate.Year - startDate.Year;
            if (endDate < startDate.AddYears(years))
            {
                years--;
            }
            return years;
        }

        // 获取指定日期所在周的第一天（默认为周一）
        public static DateTime GetFirstDayOfWeek(DateTime date, DayOfWeek firstDayOfWeek = DayOfWeek.Monday)
        {
            int diff = (7 + (date.DayOfWeek - firstDayOfWeek)) % 7;
            return date.AddDays(-1 * diff).Date;
        }

        // 获取指定日期所在周的最后一天（默认为周日）
        public static DateTime GetLastDayOfWeek(DateTime date, DayOfWeek firstDayOfWeek = DayOfWeek.Monday)
        {
            return GetFirstDayOfWeek(date, firstDayOfWeek).AddDays(6);
        }

        // 给指定日期添加指定月数，并返回新的日期
        public static DateTime AddMonthsSafe(DateTime date, int months)
        {
            int year = date.Year;
            int month = date.Month + months;
            int day = date.Day;

            while (month > 12)
            {
                month -= 12;
                year++;
            }
            while (month < 1)
            {
                month += 12;
                year--;
            }

            int maxDay = DateTime.DaysInMonth(year, month);
            if (day > maxDay)
            {
                day = maxDay;
            }

            return new DateTime(year, month, day);
        }

        // 判断指定日期是否为工作日（默认周一到周五为工作日）
        public static bool IsWeekday(DateTime date)
        {
            return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
        }

        // 判断指定日期是否为周末
        public static bool IsWeekend(DateTime date)
        {
            return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
        }

        // 获取指定日期所在季度的第一天
        public static DateTime GetFirstDayOfQuarter(DateTime date)
        {
            int quarter = (date.Month - 1) / 3 + 1;
            return new DateTime(date.Year, (quarter - 1) * 3 + 1, 1);
        }

        // 获取指定日期所在季度的最后一天
        public static DateTime GetLastDayOfQuarter(DateTime date)
        {
            int quarter = (date.Month - 1) / 3 + 1;
            int lastMonthOfQuarter = quarter * 3;
            return new DateTime(date.Year, lastMonthOfQuarter, DateTime.DaysInMonth(date.Year, lastMonthOfQuarter));
        }

        // 将 Unix 时间戳转换为 DateTime
        public static DateTime FromUnixTimestamp(long unixTimestamp)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixTimestamp);
        }

        // 将 DateTime 转换为 Unix 时间戳
        public static long ToUnixTimestamp(DateTime date)
        {
            return (long)(date.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
