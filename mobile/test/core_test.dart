import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:medsmarter_mobile/core/formatters.dart';
import 'package:medsmarter_mobile/core/models.dart';
import 'package:medsmarter_mobile/core/services.dart';

Map<String, dynamic> _json(String path) =>
    jsonDecode(File(path).readAsStringSync()) as Map<String, dynamic>;

MockServices _svc(int h, [int m = 0]) {
  final data = DemoData(_json('assets/shared/demo-data.json'));
  return MockServices(
    data,
    latency: Duration.zero,
    now: () => DateTime(2026, 9, 29, h, m),
  );
}

void main() {
  group('Formatters', () {
    const fa = Formatters(Locale('fa'));
    const en = Formatters(Locale('en'));

    test('Persian digits and percent', () {
      expect(fa.digits('08:30'), '۰۸:۳۰');
      expect(fa.percent(86), '۸۶٪');
      expect(en.percent(86), '86%');
      expect(en.number(1200), '1200');
    });

    test('Gregorian to Jalali matches known dates', () {
      expect(Formatters.toJalali(2024, 3, 20), (1403, 1, 1)); // Nowruz 1403
      expect(Formatters.toJalali(2025, 3, 21), (1404, 1, 1)); // Nowruz 1404
      expect(Formatters.toJalali(2026, 9, 29), (1405, 7, 7)); // 7 Mehr 1405
    });

    test('date and weekday strings', () {
      final d = DateTime(2026, 9, 29);
      expect(fa.date(d), '۷ مهر ۱۴۰۵');
      expect(fa.weekday(d), 'سه‌شنبه');
      expect(en.date(d), '29 September 2026');
      expect(en.weekday(d), 'Tuesday');
    });
  });

  group('Shared strings', () {
    final en = _json('assets/shared/en.json').cast<String, String>();
    final fa = _json('assets/shared/fa.json').cast<String, String>();

    test('both languages have identical keys and placeholders', () {
      expect(fa.keys.toSet(), en.keys.toSet());
      final ph = RegExp(r'\{(\w+)\}');
      for (final k in en.keys) {
        expect(
          ph.allMatches(fa[k]!).map((m) => m.group(1)).toSet(),
          ph.allMatches(en[k]!).map((m) => m.group(1)).toSet(),
          reason: k,
        );
      }
    });

    test('demo banner is labelled NOT FOR CLINICAL USE in both languages', () {
      expect(en['demo.banner'], contains('NOT FOR CLINICAL USE'));
      expect(fa['demo.banner'], contains('غیرقابل استفاده بالینی'));
    });

    test('mobile assets are byte-identical to the web copies', () {
      for (final f in ['en.json', 'fa.json', 'demo-data.json']) {
        expect(
          File('assets/shared/$f').readAsStringSync(),
          File('../web/src/shared/$f').readAsStringSync(),
          reason: f,
        );
      }
    });
  });

  group('MockServices doses', () {
    test('picks the next upcoming dose and seeds one missed dose', () async {
      final doses = await _svc(10).medications.todaysDoses('pt-sara');
      expect(doses.firstWhere((d) => d.isNext).time, '13:00');
      expect(doses.any((d) => d.status == DoseStatus.missed), isTrue);
    });

    test('rolls over to tomorrow after the last dose', () async {
      final doses = await _svc(23, 30).medications.todaysDoses('pt-sara');
      final next = doses.firstWhere((d) => d.isNext);
      expect(next.tomorrow, isTrue);
      expect(next.time, '08:00');
    });

    test('records and undoes a dose', () async {
      final s = _svc(7);
      final next = (await s.medications.todaysDoses('pt-sara'))
          .firstWhere((d) => d.isNext);
      final after = await s.medications.setDose(next.id, DoseStatus.taken);
      expect(after.firstWhere((d) => d.id == next.id).status, DoseStatus.taken);
      final undone = await s.medications.setDose(next.id, DoseStatus.upcoming);
      expect(
        undone.firstWhere((d) => d.id == next.id).status,
        DoseStatus.upcoming,
      );
    });
  });

  group('MockServices AI (no LLM)', () {
    Future<AssistantAnswer> ask(String q, {String? drug}) =>
        _svc(10).ai.ask(text: q, locale: 'en', contextDrugId: drug);

    test('answers from demo sources with confidence and evidence', () async {
      final a = await ask('What should I do if I miss a dose?');
      expect(a.kind, AnswerKind.answer);
      expect(a.sources, isNotEmpty);
      expect(a.evidence, isNotEmpty);
    });

    test('refuses instead of guessing', () async {
      final a = await ask('Explain quantum tunnelling');
      expect(a.kind, AnswerKind.refusal);
      expect(a.sources, isEmpty);
      expect(a.confidence, Confidence.low);
      expect(a.canEscalate, isTrue);
    });

    test('red flags return static guidance (English and Persian)', () async {
      expect((await ask('I have chest pain')).kind, AnswerKind.redFlag);
      expect(
        (await _svc(10).ai.ask(text: 'درد قفسه سینه دارم', locale: 'fa')).kind,
        AnswerKind.redFlag,
      );
    });

    test('drug context resolves short questions', () async {
      final a = await ask('What are the warnings?', drug: 'drug-nocturin');
      expect(a.text.en, contains('Nocturin'));
    });

    test('flags the fictional Demopril + Nocturin interaction', () async {
      final ix = await _svc(10).prescriptions.interactionsForPatient('pt-sara');
      expect(ix.map((x) => x.id), contains('ix-1'));
    });
  });

  group('DEMO data hygiene', () {
    test('every active ingredient is marked fictional', () {
      final data = DemoData(_json('assets/shared/demo-data.json'));
      expect(
        data.ingredients.values.every((a) => a.name.en.contains('(fictional)')),
        isTrue,
      );
    });
  });
}
