using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Logging;
using HDBF.Gameplay.Components;
using Timberborn.BaseComponentSystem;
using Timberborn.BehaviorSystem;
using Timberborn.BlockSystem;
using Timberborn.Carrying;
using Timberborn.Fields;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.Planting;
using Timberborn.WorkSystem;
using Timberborn.Yielding;
using UnityEngine;

namespace HDBF.Gameplay.Farming
{
    /// <summary>
    /// The farm's work dispatcher: one <see cref="WorkplaceBehavior"/> shared by all four tiers
    /// (REQ-ARCH-07). It replaces the vanilla farmhouse behavior instead of patching it
    /// (REQ-IMPL-15: zero Harmony patches) and reuses the vanilla worker-side task steps for the
    /// actual walking/planting/harvesting so no item is ever spawned by HDBF code:
    ///
    /// * planting: the plot to work is chosen by <see cref="HdbfBoxFarmController"/> (daily budget,
    ///   harvest-before-seed, real path length); this behavior reserves exactly that cell on the
    ///   worker's vanilla <see cref="Planter"/> and then hands control to the vanilla
    ///   <see cref="PlantBehavior"/> (its <c>PlantExecutor</c> walks to the cell and calls
    ///   <c>NaturalResourceFactory.PlantNew</c>, which is the real crop entity);
    /// * harvesting: the mature crop's vanilla <see cref="Yielder"/> is selected by the controller,
    ///   capacity is reserved in the building's <see cref="Inventory"/> and on the worker's
    ///   <see cref="YielderRemover"/>, then the vanilla <see cref="YieldRemoverBehavior"/> walks,
    ///   removes the yield and deposits it — the harvest goods therefore only ever come from a
    ///   crop that really grew on a managed plot (REQ-BAL-22/23).
    ///
    /// Every game-object call is inside try/catch; a fault logs once per throttled key and returns
    /// <see cref="Decision.ReleaseNow"/> so the simulation loop never sees the exception
    /// (REQ-SAFE-01/02). After repeated faults the controller disables farming for that building.
    /// </summary>
    public class HdbfFarmWorkplaceBehavior : WorkplaceBehavior, IAwakableComponent
    {
        private const string LogKey = "gameplay.farm";
        private const int MaxTrackedHarvests = 64;

        private readonly IBlockService _blockService;
        private readonly IHdbfLog _log;
        private readonly Dictionary<Worker, PendingHarvest> _pendingHarvests =
            new Dictionary<Worker, PendingHarvest>();

        private HdbfBoxFarmController? _controller;
        private Workplace? _workplace;

        public HdbfFarmWorkplaceBehavior(IBlockService blockService, IHdbfLog log)
        {
            _blockService = blockService;
            _log = log;
        }

        /// <inheritdoc />
        public void Awake()
        {
            try
            {
                _controller = HasComponent<HdbfBoxFarmController>() ? GetComponent<HdbfBoxFarmController>() : null;
                _workplace = HasComponent<Workplace>() ? GetComponent<Workplace>() : null;
            }
            catch (Exception exception)
            {
                _controller = null;
                _log.Error(HdbfLogCategory.Error, LogKey,
                    "farm workplace behavior failed to initialize; farming disabled for this building",
                    exception);
            }
        }

        /// <inheritdoc />
        public override Decision Decide(BehaviorAgent agent)
        {
            try
            {
                var controller = _controller;
                if (controller is null || _workplace is null || !controller.IsFarmWorkAvailable)
                {
                    return Decision.ReleaseNow();
                }

                var worker = agent.GetComponent<Worker>();
                if (worker is null || worker.Workplace is null)
                {
                    return Decision.ReleaseNow();
                }

                // A finished/partial harvest only becomes visible when the worker is idle again, so
                // this is where the fatigue bookkeeping and the plot claim release happen.
                FinalizeHarvest(worker, controller);

                if (!controller.TryTakeFarmWorkItem(out var item))
                {
                    return Decision.ReleaseNow();
                }

                return item.Kind == FarmActionKind.Harvest
                    ? StartHarvest(agent, worker, item, controller)
                    : StartPlant(agent, worker, item, controller);
            }
            catch (Exception exception)
            {
                _controller?.ReportFarmFault(exception, "workplace behavior");
                return Decision.ReleaseNow();
            }
        }

        // ------------------------------------------------------------------ planting

        private Decision StartPlant(BehaviorAgent agent, Worker worker, FarmWorkItem item,
            HdbfBoxFarmController controller)
        {
            var planter = worker.GetComponent<Planter>();
            var plantBehavior = worker.GetComponent<PlantBehavior>();
            if (planter is null || plantBehavior is null)
            {
                controller.NotifyFarmActionFailed(item, "worker has no vanilla Planter/PlantBehavior");
                return Decision.ReleaseNow();
            }

            // A stale reservation from a previous interrupted action must never leak into the next
            // one: vanilla PlantBehavior only searches for a spot when the Planter is free.
            if (planter.PlantingCoordinates.HasValue)
            {
                planter.Unreserve();
            }

            if (!controller.EnsurePlantingCrop(item.PlotId, out var cropTemplate)
                || string.IsNullOrEmpty(cropTemplate))
            {
                controller.NotifyFarmActionFailed(item, "no crop template available for the plot");
                return Decision.ReleaseNow();
            }

            planter.Reserve(item.Cell);
            var decision = plantBehavior.StartPlanting(agent);
            if (decision.ShouldReleaseNow)
            {
                planter.Unreserve();
                controller.NotifyFarmActionFailed(item, "vanilla planting step refused to start");
                return Decision.ReleaseNow();
            }

            return Decision.TransferNow(plantBehavior, in decision);
        }

        // ------------------------------------------------------------------ harvesting

        private Decision StartHarvest(BehaviorAgent agent, Worker worker, FarmWorkItem item,
            HdbfBoxFarmController controller)
        {
            var yielder = _blockService.GetFirstObjectWithComponentAt<Yielder>(item.Cell);
            if (yielder is null || !yielder.IsYielding)
            {
                controller.NotifyFarmActionFailed(item, "plot has no yielding crop any more");
                return Decision.ReleaseNow();
            }

            var inventory = controller.TryGetFarmOutputInventory();
            var carrier = worker.GetComponent<GoodCarrier>();
            var reserver = worker.GetComponent<GoodReserver>();
            var remover = worker.GetComponent<YielderRemover>();
            var removerBehavior = worker.GetComponent<YieldRemoverBehavior>();
            if (inventory is null || carrier is null || reserver is null || remover is null
                || removerBehavior is null)
            {
                controller.NotifyFarmActionFailed(item,
                    "harvest prerequisites missing (inventory or vanilla harvester components)");
                return Decision.ReleaseNow();
            }

            var available = yielder.Yield;
            var amount = available.Amount;
            var liftingCapacity = carrier.LiftingCapacity;
            if (liftingCapacity > 0 && amount > liftingCapacity)
            {
                // Vanilla harvests at most one carry load per trip and repeats for the remainder.
                amount = liftingCapacity;
            }

            if (amount <= 0 || string.IsNullOrEmpty(available.GoodId))
            {
                controller.NotifyFarmActionFailed(item, "crop yield is empty");
                return Decision.ReleaseNow();
            }

            if (remover.HasReservedYielder)
            {
                remover.Unreserve();
            }

            if (reserver.HasReservedCapacity)
            {
                reserver.UnreserveCapacity();
            }

            var yield = new GoodAmount(available.GoodId, amount);
            if (!inventory.HasUnreservedCapacity(yield))
            {
                // The internal output inventory is full: let the vanilla haulers drain it first
                // instead of letting the inventory throw from inside the behaviour loop.
                controller.NotifyFarmActionFailed(item, "output inventory is full");
                return Decision.ReleaseNow();
            }

            reserver.ReserveCapacity(inventory, yield);
            remover.ReserveForRemoval(yielder, yield);
            TrackHarvest(worker, item, yielder, available.Amount);

            var decision = removerBehavior.Decide(agent);
            if (decision.ShouldReleaseNow)
            {
                remover.Unreserve();
                reserver.UnreserveCapacity();
                _pendingHarvests.Remove(worker);
                controller.ReleaseFarmClaim(item.PlotId);
                controller.NotifyFarmActionFailed(item, "vanilla harvest step refused to start");
                return Decision.ReleaseNow();
            }

            return Decision.TransferNow(removerBehavior, in decision);
        }

        private void TrackHarvest(Worker worker, FarmWorkItem item, Yielder yielder, int initialAmount)
        {
            if (_pendingHarvests.Count >= MaxTrackedHarvests)
            {
                _pendingHarvests.Clear();
            }

            _pendingHarvests[worker] = new PendingHarvest(item.PlotId, item.CropTemplate, yielder, initialAmount);
        }

        /// <summary>
        /// Detects the yield removal that happened during the previous action. Vanilla harvests one
        /// carry load at a time, so a crop with a multi-unit yield is harvested over several trips:
        /// fatigue is applied exactly once, when the crop stops yielding (chapter 6.4).
        /// </summary>
        private void FinalizeHarvest(Worker worker, HdbfBoxFarmController controller)
        {
            if (!_pendingHarvests.TryGetValue(worker, out var pending))
            {
                return;
            }

            _pendingHarvests.Remove(worker);
            controller.ReleaseFarmClaim(pending.PlotId);

            try
            {
                var yielder = pending.Yielder;
                if (yielder is null)
                {
                    // The crop entity disappeared during/after the action; treat it as harvested
                    // when we know the crop had yield to remove.
                    if (pending.InitialAmount > 0)
                    {
                        controller.NotifyPlotHarvested(pending.PlotId, pending.CropTemplate);
                    }

                    return;
                }

                var currentAmount = yielder.Yield.Amount;
                var removedSomething = currentAmount < pending.InitialAmount;
                var exhausted = yielder.IsYieldRemoved || !yielder.IsYielding || currentAmount <= 0;
                if (removedSomething && exhausted)
                {
                    controller.NotifyPlotHarvested(pending.PlotId, pending.CropTemplate);
                }
            }
            catch (Exception exception)
            {
                controller.ReportFarmFault(exception, "harvest finalize");
            }
        }

        private readonly struct PendingHarvest
        {
            public PendingHarvest(int plotId, string cropTemplate, Yielder yielder, int initialAmount)
            {
                PlotId = plotId;
                CropTemplate = cropTemplate ?? string.Empty;
                Yielder = yielder;
                InitialAmount = initialAmount;
            }

            public int PlotId { get; }

            public string CropTemplate { get; }

            public Yielder Yielder { get; }

            public int InitialAmount { get; }
        }
    }
}
