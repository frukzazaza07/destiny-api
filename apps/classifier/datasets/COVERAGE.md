# Classifier dataset coverage

Generated: 2026-08-30

`bootstrap-v2.json` contains 11,836 bilingual records using the fixed
`TAXONOMY_V1` taxonomy and `tarot-classifier-dataset-v2` schema.

| Cohort | Per intent / locale | Total | Review state |
| --- | ---: | ---: | --- |
| Important intents | 300 | included below | `PENDING` synthetic bootstrap |
| Lower-volume intents | 100 | included below | `PENDING` synthetic bootstrap |
| `PERSONAL_CUSTOM` / hard negatives | n/a | 1,000 | `PENDING` hard negatives |
| Curated seed truth | varies | 236 | `APPROVED` seed |
| All pending review candidates | n/a | 11,600 | `PENDING` |

The volume targets are represented as review candidates, but they are intentionally
not counted as reviewed production truth. A human reviewer must approve or correct
each candidate before it enters training/evaluation. Consequently the Stage A quality
gate remains closed and `DeepSharedCache` plus `StartupCacheWarmup` remain disabled.

The local candidate evaluation currently reports 40 approved seed examples in the
test partition with 100% accepted precision, but `qualityGatePassed` is `false`
because the required reviewed rejection/subset coverage and approved-intent list are
absent. This small seed result is not sufficient for rollout approval.
