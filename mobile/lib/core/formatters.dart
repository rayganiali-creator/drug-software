import 'package:flutter/widgets.dart';

/// Isolates a left-to-right run (doses like "500 mg") so it keeps its order inside RTL text.
String isolateLtr(String s) => '\u2066$s\u2069';

/// Locale-aware number/date helpers. Persian uses Persian digits and the Jalali calendar,
/// matching the web `Intl` output ("fa-IR-u-ca-persian").
class Formatters {
  const Formatters(this.locale);
  final Locale locale;
  bool get isFa => locale.languageCode == 'fa';

  static const _faDigits = '۰۱۲۳۴۵۶۷۸۹';

  String digits(String s) => isFa
      ? s.replaceAllMapped(
          RegExp(r'\d'),
          (m) => _faDigits[int.parse(m.group(0)!)],
        )
      : s;
  String number(num n) => digits(n is int ? '$n' : n.toString());
  String percent(num n) => '${number(n)}${isFa ? '٪' : '%'}';
  String time(String hhmm) => digits(hhmm);

  static const _faWeek = [
    'دوشنبه',
    'سه‌شنبه',
    'چهارشنبه',
    'پنجشنبه',
    'جمعه',
    'شنبه',
    'یکشنبه',
  ];
  static const _enWeek = [
    'Monday',
    'Tuesday',
    'Wednesday',
    'Thursday',
    'Friday',
    'Saturday',
    'Sunday',
  ];
  static const _faMonths = [
    'فروردین',
    'اردیبهشت',
    'خرداد',
    'تیر',
    'مرداد',
    'شهریور',
    'مهر',
    'آبان',
    'آذر',
    'دی',
    'بهمن',
    'اسفند',
  ];
  static const _enMonths = [
    'January',
    'February',
    'March',
    'April',
    'May',
    'June',
    'July',
    'August',
    'September',
    'October',
    'November',
    'December',
  ];

  String relativeMinutes(
    int m,
    String Function(String key, Map<String, Object> params) t,
  ) {
    if (m < 1) return t('time.justNow', const {});
    if (m < 60) return t('time.minutesAgo', {'n': digits('$m')});
    if (m < 1440) {
      return t('time.hoursAgo', {'n': digits('${(m / 60).round()}')});
    }
    return t('time.daysAgo', {'n': digits('${(m / 1440).round()}')});
  }

  String weekday(DateTime d) => (isFa ? _faWeek : _enWeek)[d.weekday - 1];

  String date(DateTime d) {
    if (!isFa) return '${d.day} ${_enMonths[d.month - 1]} ${d.year}';
    final j = toJalali(d.year, d.month, d.day);
    return '${digits('${j.$3}')} ${_faMonths[j.$2 - 1]} ${digits('${j.$1}')}';
  }

  String monthShort(DateTime d) => isFa
      ? _faMonths[toJalali(d.year, d.month, d.day).$2 - 1]
      : _enMonths[d.month - 1].substring(0, 3);

  /// Gregorian -> Jalali (year, month, day).
  static (int, int, int) toJalali(int gy, int gm, int gd) {
    const gdm = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];
    final gy2 = gm > 2 ? gy + 1 : gy;
    var days =
        355666 +
        (365 * gy) +
        ((gy2 + 3) ~/ 4) -
        ((gy2 + 99) ~/ 100) +
        ((gy2 + 399) ~/ 400) +
        gd +
        gdm[gm - 1];
    var jy = -1595 + (33 * (days ~/ 12053));
    days %= 12053;
    jy += 4 * (days ~/ 1461);
    days %= 1461;
    if (days > 365) {
      jy += (days - 1) ~/ 365;
      days = (days - 1) % 365;
    }
    final jm = days < 186 ? 1 + (days ~/ 31) : 7 + ((days - 186) ~/ 30);
    final jd = 1 + (days < 186 ? days % 31 : (days - 186) % 30);
    return (jy, jm, jd);
  }
}
