using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;

namespace Content.Shared.Paper;
public sealed class PenInkSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<PenInkComponent, PaperWriteEvent>(OnPaperWrite);
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
}