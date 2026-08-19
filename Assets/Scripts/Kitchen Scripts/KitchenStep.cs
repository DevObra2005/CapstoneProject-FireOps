// -------------------------------------------------------
// KitchenStep.cs
//
// Kitchen's own step sequence, SEPARATE from SimulationInteractable.SimStep.
//
// WHY A SECOND ENUM INSTEAD OF EXTENDING SimStep:
// SimulationManager advances with a plain `currentStep++`. Adding Kitchen
// values on the end of SimStep (9, 10, 11...) would mean the counter never
// reaches them - it counts 1, 2, 3, 4 and stops at maxStep.
//
// Worse, SimulationInteractable compares step numbers with > and < to decide
// whether a tap is "too early" or "already done". Non-sequential values break
// that comparison SILENTLY - no error, just wrong penalties.
//
// So Kitchen gets its own 1..6. Both enums are only ever used as STRINGS by
// SimulationManager (step.ToString()), so nothing downstream cares which enum
// a name came from.
//
// WCTL = Wet, Cover, Turn off, Leave.
// "Leave" is Evacuate here, and like Office it is completed by the exit door
// rather than a button - which is why only Wet, Cover and TurnOff appear in
// WCTLButtonManager's array.
// -------------------------------------------------------

public enum KitchenStep
{
    GrabTowel = 1,
    WCTL_Wet = 2,
    WCTL_Cover = 3,
    WCTL_TurnOff = 4,
    Evacuate = 5
}