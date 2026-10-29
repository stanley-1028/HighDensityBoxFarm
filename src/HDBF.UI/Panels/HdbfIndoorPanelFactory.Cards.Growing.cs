// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-05
// Purpose: "Growing now" card of the v2 indoor panel read model (REQ-V2-UI-05): a live summary row
//          (planted/capacity slots + average growth bar) followed by one row per crop that currently
//          holds slots (localized name, growing/ready split, average growth bar and tooltip), or the
//          single "nothing planted yet" row when the farm is empty. The card sits directly under the
//          worker card so a partly planted farm is visible without scrolling. Split out of
//          HdbfIndoorPanelFactory.Cards.cs so every file stays inside the 300-line gate; the
//          aggregation itself lives in HDBF.Core.IndoorCropReport.
// Requirements: REQ-V2-UI-05, REQ-V2-UI-01, REQ-V2-READ-04, REQ-V2-PERF-02
// ============================================================================
using System;
using System.Collections.Generic;
using HDBF.Core.Formatting;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    public static partial class HdbfIndoorPanelFactory
    {
        private static HdbfPanelSection BuildGrowingSection(IndoorFarmSnapshot snapshot,
            Func<string, string> localize)
        {
            var crops = snapshot.Crops;
            var plannedRows = crops is null ? 1 : crops.Count + 1;
            var rows = new List<HdbfPanelRow>(plannedRows);
            var total = 0;
            var progressSum = 0f;
            var timeSum = 0f;
            if (crops is not null)
            {
                for (var index = 0; index < crops.Count; index++)
                {
                    var crop = crops[index];
                    if (crop is null || crop.TotalSlots <= 0)
                    {
                        continue;
                    }

                    total += crop.TotalSlots;
                    progressSum += crop.AverageProgress * crop.TotalSlots;
                    timeSum += crop.GrowthTimeInDays * crop.TotalSlots;
                }
            }

            if (total > 0)
            {
                // REQ-V2-UI-05: the summary row is the "dynamic quantity" the player watches — it is
                // filled/total slots plus the averaged growth of every occupied slot of THIS building.
                var average = HdbfIndoorPanelLimits.Fill(progressSum / total);
                var percent = PanelNumbers.Percent(average, roundUp: false);
                rows.Add(new HdbfPanelRow
                {
                    IconKey = "growing",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelGrowingPlanted),
                    Value = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelGrowingPlantedValue,
                        total, snapshot.Capacity),
                    Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipGrowing,
                        total, percent, PanelNumbers.Days(timeSum / total, roundUp: false)),
                    HasBar = true,
                    Bar = average,
                    BarText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPercentValue,
                        percent),
                });
            }

            if (crops is not null)
            {
                for (var index = 0; index < crops.Count; index++)
                {
                    var crop = crops[index];
                    if (crop is null)
                    {
                        continue;
                    }

                    var percent = PanelNumbers.Percent(crop.AverageProgress, roundUp: false);
                    rows.Add(new HdbfPanelRow
                    {
                        IconKey = "crop",
                        Label = HdbfIndoorPanelFormat.CropName(localize, crop.CropId),
                        Value = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelGrowingValue,
                            crop.GrowingSlots, crop.MatureSlots),
                        Tooltip = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.TooltipGrowing,
                            crop.TotalSlots, percent,
                            PanelNumbers.Days(crop.GrowthTimeInDays, roundUp: false)),
                        HasBar = true,
                        Bar = HdbfIndoorPanelLimits.Fill(crop.AverageProgress),
                        BarText = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelPercentValue,
                            percent),
                    });
                }
            }

            if (rows.Count == 0)
            {
                // REQ-V2-UI-05: an empty farm keeps the card visible with one explanatory row; the
                // value is the shared dash so a non-warning row never renders an empty value.
                rows.Add(new HdbfPanelRow
                {
                    IconKey = "empty",
                    Label = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelGrowingEmpty),
                    Value = HdbfIndoorPanelFormat.Unknown,
                });
            }

            return new HdbfPanelSection
            {
                Kind = HdbfPanelSectionKind.Growing,
                Title = HdbfIndoorPanelFormat.Text(localize, HdbfUiLocKeys.PanelSectionGrowing),
                IconKey = "growing",
                Tone = "green",
                Rows = rows,
            };
        }
    }
}
