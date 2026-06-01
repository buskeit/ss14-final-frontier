using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Examine;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;

namespace Content.Shared.Paper;

public sealed class PenInkSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PenInkComponent, PaperWriteEvent>(OnPaperWrite);
        SubscribeLocalEvent<PenInkComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PenInkComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnPaperWrite(
        Entity<PenInkComponent> ent,
        ref PaperWriteEvent args)
    {
        if (!_solutionContainer.TryGetSolution(
            ent.Owner,
            ent.Comp.SolutionName,
            out var solutionEnt,
            out var solution))
            return;

        if (solution.Volume <= 0)
        {
            Logger.Info("Pen is dry!");
            return;
        }

        _solutionContainer.RemoveReagent(
            solutionEnt.Value,
            "InkBlack",
            FixedPoint2.New(1));

        Logger.Info($"Ink remaining: {solution.Volume}");
    }

    private void OnExamined(Entity<PenInkComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out _, out var solution))
            return;

        var remaining = (int) solution.Volume.Float();
        var max = (int) solution.MaxVolume.Float();

        args.PushMarkup(Loc.GetString("pen-ink-examine",
            ("remaining", remaining),
            ("max", max)));
    }

    private void OnInteractUsing(Entity<PenInkComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<InkCartridgeComponent>(args.Used, out var cartridge))
            return;

        args.Handled = true;

        if (!ent.Comp.Refillable)
        {
            _popup.PopupClient(Loc.GetString("pen-ink-refill-not-refillable", ("pen", ent.Owner)), ent.Owner, args.User);
            return;
        }

        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var penSolEnt, out var penSol))
            return;

        if (!_solutionContainer.TryGetSolution(args.Used, cartridge.SolutionName, out var cartSolEnt, out var cartSol))
            return;

        if (penSol.Volume >= penSol.MaxVolume)
        {
            _popup.PopupClient(Loc.GetString("pen-ink-refill-already-full", ("pen", ent.Owner)), ent.Owner, args.User);
            return;
        }

        var inkQuantity = cartSol.GetReagentQuantity(new ReagentId("InkBlack", null));
        if (inkQuantity <= 0)
        {
            _popup.PopupClient(Loc.GetString("pen-ink-refill-cartridge-empty", ("cartridge", args.Used)), ent.Owner, args.User);
            return;
        }

        var spaceRemaining = penSol.MaxVolume - penSol.Volume;
        var transferAmount = FixedPoint2.Min(spaceRemaining, inkQuantity);

        if (transferAmount <= 0)
            return;

        _solutionContainer.RemoveReagent(cartSolEnt.Value, "InkBlack", transferAmount);
        _solutionContainer.TryAddReagent(penSolEnt.Value, "InkBlack", transferAmount, out _);

        _popup.PopupClient(Loc.GetString("pen-ink-refill-success", ("pen", ent.Owner), ("cartridge", args.Used)), ent.Owner, args.User);
        _audio.PlayPredicted(new SoundPathSpecifier("/Audio/Items/pen_click.ogg"), ent.Owner, args.User);
    }
}