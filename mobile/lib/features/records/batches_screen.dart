import 'package:flutter/material.dart';

import '../../api/records_client.dart';
import '../../components/buttons.dart';
import '../../components/display.dart';
import '../../components/health.dart';
import '../../components/inputs.dart';
import '../../components/screen.dart';
import '../../components/tone.dart';
import '../../core/app_scope.dart';
import '../../design/theme.dart';
import '../../design/tokens.g.dart';
import 'common.dart';

const _statusTone = {
  'Draft': Tone.neutral,
  'PendingConsentOrReview': Tone.warning,
  'ReadyToSend': Tone.info,
  'Sent': Tone.success,
  'Acknowledged': Tone.success,
  'Failed': Tone.danger,
  'Cancelled': Tone.neutral,
};

/// Batch / lot records and reports to the manufacturer. Scanning is only a connection point (not built); codes are typed,
/// never invented. A report is de-identified on the server and nothing leaves the system without consent and review.
class BatchesScreen extends StatefulWidget {
  const BatchesScreen({super.key});
  @override
  State<BatchesScreen> createState() => _BatchesScreenState();
}

class _BatchesScreenState extends State<BatchesScreen> {
  int _gen = 0;
  void _refresh() => setState(() => _gen++);

  @override
  Widget build(BuildContext context) {
    final api = context.app.records;
    return ScreenScaffold(
      title: context.t('rec.batch.title'),
      subtitle: context.t('rec.batch.sub'),
      children: [
        RecordsLoader<List<Json>>(
          key: ValueKey('products$_gen'),
          load: api.products,
          builder: (context, products, _) => Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              RecSection(
                title: context.t('rec.batch.list'),
                child: products.isEmpty
                    ? EmptyState(
                        icon: 'box',
                        title: context.t('rec.batch.empty'),
                        body: context.t('rec.batch.emptyBody'),
                      )
                    : Column(
                        children: [for (final p in products) _ProductRow(p: p)],
                      ),
              ),
              _AddProduct(onAdded: _refresh),
              _NewReport(products: products, onCreated: _refresh),
            ],
          ),
        ),
        RecordsLoader<List<Json>>(
          key: ValueKey('reports$_gen'),
          load: api.reports,
          builder: (context, reports, _) => RecSection(
            title: context.t('rec.rep.list'),
            subtitle: context.t('rec.rep.sub'),
            child: reports.isEmpty
                ? EmptyState(
                    icon: 'fileText',
                    title: context.t('rec.rep.empty'),
                    body: context.t('rec.rep.emptyBody'),
                  )
                : Column(
                    children: [
                      for (final r in reports)
                        _ReportRow(r: r, onChanged: _refresh),
                    ],
                  ),
          ),
        ),
      ],
    );
  }
}

class _ProductRow extends StatelessWidget {
  const _ProductRow({required this.p});
  final Json p;
  @override
  Widget build(BuildContext context) {
    final expiry = DateTime.tryParse('${p['expiryDate']}');
    final findings = [for (final f in (p['findings'] as List? ?? [])) '$f'];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AppListTile(
          title: '${p['productName']}',
          meta:
              '${context.t('rec.batch.number')}: ${p['batchNumber']} · ${context.t('rec.batch.expiry')}: ${expiry == null ? p['expiryDate'] : context.fmt.date(expiry)}',
          trailing: Wrap(
            spacing: Space.s2,
            children: [
              if (p['expired'] == true)
                AppBadge(
                  text: context.t('rec.batch.expired'),
                  tone: Tone.danger,
                  icon: 'alertTriangle',
                ),
              if (p['isDemo'] == true) const DemoBadge(),
            ],
          ),
        ),
        for (final f in findings)
          Padding(
            padding: const EdgeInsetsDirectional.symmetric(horizontal: Space.s4),
            child: Text(
              '• ${_finding(context, f)}',
              style: context.text.caption,
            ),
          ),
      ],
    );
  }

  String _finding(BuildContext context, String f) {
    final key = 'rec.finding.${f.replaceAll('.', '_')}';
    return context.app.strings.has(key) ? context.t(key) : f;
  }
}

class _AddProduct extends StatefulWidget {
  const _AddProduct({required this.onAdded});
  final VoidCallback onAdded;
  @override
  State<_AddProduct> createState() => _AddProductState();
}

class _AddProductState extends State<_AddProduct> with ActionRunner {
  final _name = TextEditingController();
  final _batch = TextEditingController();
  final _expiry = TextEditingController();

  @override
  void dispose() {
    _name.dispose();
    _batch.dispose();
    _expiry.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => RecSection(
    title: context.t('rec.batch.add'),
    subtitle: context.t('rec.batch.addSub'),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AppTextField(label: context.t('rec.batch.typedName'), controller: _name),
        const SizedBox(height: Space.s3),
        AppTextField(label: context.t('rec.batch.number'), controller: _batch),
        const SizedBox(height: Space.s3),
        AppTextField(
          label: context.t('rec.batch.expiry'),
          hint: 'YYYY-MM-DD',
          controller: _expiry,
          keyboardType: TextInputType.datetime,
        ),
        const SizedBox(height: Space.s2),
        Text(context.t('rec.batch.noInvent'), style: context.text.caption),
        const SizedBox(height: Space.s3),
        Wrap(
          spacing: Space.s2,
          children: [
            AppButton(
              label: context.t('rec.add'),
              iconStart: 'plus',
              loading: busy,
              onPressed: () {
                final name = _name.text.trim();
                final batch = _batch.text.trim();
                final expiry = _expiry.text.trim();
                if (name.isEmpty || batch.isEmpty || expiry.isEmpty) return;
                act(
                  () => context.app.records.addProduct(
                    name: name,
                    batch: batch,
                    expiry: expiry,
                  ),
                  () {
                    _name.clear();
                    _batch.clear();
                    _expiry.clear();
                    widget.onAdded();
                  },
                );
              },
            ),
            AppButton(
              label: context.t('rec.scan.button'),
              variant: ButtonVariant.secondary,
              iconStart: 'search',
              onPressed: null, // a future connection point, not built
            ),
          ],
        ),
        failureAlert(),
      ],
    ),
  );
}

class _NewReport extends StatefulWidget {
  const _NewReport({required this.products, required this.onCreated});
  final List<Json> products;
  final VoidCallback onCreated;
  @override
  State<_NewReport> createState() => _NewReportState();
}

class _NewReportState extends State<_NewReport> with ActionRunner {
  String? _product;
  String _issue = 'AbnormalAppearanceOrPackaging';
  String _severity = 'Mild';
  final _text = TextEditingController();

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (widget.products.isEmpty) {
      return RecSection(
        title: context.t('rec.rep.new'),
        child: Text(context.t('rec.rep.needProduct')),
      );
    }
    final selected = _product ?? '${widget.products.first['id']}';
    return RecSection(
      title: context.t('rec.rep.new'),
      subtitle: context.t('rec.rep.newSub'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          AppSelect<String>(
            label: context.t('rec.rep.product'),
            value: selected,
            items: {
              for (final p in widget.products)
                '${p['id']}': '${p['productName']} · ${p['batchNumber']}',
            },
            onChanged: (v) => setState(() => _product = v),
          ),
          const SizedBox(height: Space.s3),
          AppSelect<String>(
            label: context.t('rec.rep.issue'),
            value: _issue,
            items: {
              for (final i in [
                'AdverseEvent',
                'AbnormalAppearanceOrPackaging',
                'ApparentLackOfEffect',
                'QualityProblem',
                'Other',
              ])
                i: context.t('rec.issue.$i'),
            },
            onChanged: (v) => setState(() => _issue = v),
          ),
          const SizedBox(height: Space.s3),
          AppSelect<String>(
            label: context.t('rec.rep.severity'),
            value: _severity,
            items: {
              for (final s in ['Unknown', 'Mild', 'Moderate', 'Severe'])
                s: context.t('rec.severity.$s'),
            },
            onChanged: (v) => setState(() => _severity = v),
          ),
          const SizedBox(height: Space.s3),
          AppTextField(
            label: context.t('rec.rep.description'),
            hint: context.t('rec.rep.descHint'),
            controller: _text,
            minLines: 2,
            maxLines: 5,
          ),
          const SizedBox(height: Space.s3),
          AppButton(
            label: context.t('rec.rep.create'),
            loading: busy,
            onPressed: () => act(
              () => context.app.records.createReport(
                productRecordId: selected,
                issueType: _issue,
                severity: _severity,
                description: _text.text.trim().isEmpty ? null : _text.text.trim(),
              ),
              () {
                _text.clear();
                widget.onCreated();
              },
            ),
          ),
          failureAlert(),
        ],
      ),
    );
  }
}

class _ReportRow extends StatefulWidget {
  const _ReportRow({required this.r, required this.onChanged});
  final Json r;
  final VoidCallback onChanged;
  @override
  State<_ReportRow> createState() => _ReportRowState();
}

class _ReportRowState extends State<_ReportRow> with ActionRunner {
  @override
  Widget build(BuildContext context) {
    final r = widget.r;
    final status = '${r['status']}';
    final api = context.app.records;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AppListTile(
          title: '${r['productName']} · ${r['batchNumber']}',
          meta: context.t('rec.issue.${r['issueType']}'),
          trailing: AppBadge(
            text: context.t('rec.rep.status.$status'),
            tone: _statusTone[status] ?? Tone.neutral,
          ),
        ),
        if (r['isMockDelivery'] == true)
          Padding(
            padding: const EdgeInsetsDirectional.symmetric(horizontal: Space.s4),
            child: Text(context.t('rec.rep.mockDelivery'), style: context.text.caption),
          ),
        if (r['reviewRequired'] == true && status == 'PendingConsentOrReview')
          Padding(
            padding: const EdgeInsetsDirectional.symmetric(horizontal: Space.s4),
            child: Text(context.t('rec.rep.needsReview'), style: context.text.caption),
          ),
        Padding(
          padding: const EdgeInsetsDirectional.only(start: Space.s4),
          child: Wrap(
            spacing: Space.s2,
            children: [
              if (status == 'Draft')
                AppButton(
                  label: context.t('rec.rep.submit'),
                  size: ButtonSize.sm,
                  loading: busy,
                  onPressed: () => act(
                    () => api.submitReport('${r['id']}', (r['version'] as num).toInt()),
                    widget.onChanged,
                  ),
                ),
              if (status == 'Draft' || status == 'PendingConsentOrReview')
                AppButton(
                  label: context.t('rec.rep.cancel'),
                  size: ButtonSize.sm,
                  variant: ButtonVariant.ghost,
                  onPressed: busy
                      ? null
                      : () => act(() => api.cancelReport('${r['id']}'), widget.onChanged),
                ),
            ],
          ),
        ),
        failureAlert(),
      ],
    );
  }
}
