using UnityEngine;

public sealed partial class WebFightBootstrap
{
    private bool deferCombatContacts;
    private enum Contact { None, Normal, Heavy, Grab }

    private Contact PendingContact(Fighter fighter, Fighter opponent)
    {
        var s = fighter.State;
        if (s.GrabRequested)
        {
            var other = opponent.State;
            bool facing = s.X < other.X ? other.FacingDirection < 0 : other.FacingDirection > 0;
            return !other.IsGrabbed && !other.IsGrabbing && (!other.IsGuarding || !facing) && CheckCollision(BuildGrabBox(s), BuildPlayerBox(other)) ? Contact.Grab : Contact.None;
        }
        if (!s.ContactReady || s.HasHit || s.AttackBoxes.Count == 0) return Contact.None;
        s.AttackBoxes.Clear();
        BuildAttackBoxes(s, s.AttackType, s.AttackDirection, s.AttackBoxes);
        foreach (var box in s.AttackBoxes)
            if (CheckCollision(box, BuildPlayerBox(opponent.State))) return s.AttackType == AttackType.Heavy ? Contact.Heavy : Contact.Normal;
        return Contact.None;
    }

    private static bool Beats(Contact first, Contact second)
    {
        return (first == Contact.Heavy && second == Contact.Normal) ||
            (first == Contact.Grab && second == Contact.Heavy) ||
            (first == Contact.Normal && second == Contact.Grab);
    }

    private static void CancelContact(FighterState state)
    {
        state.AttackTimer = 0;
        state.AttackType = AttackType.None;
        state.AttackBoxes.Clear();
        state.HasHit = true;
        state.GrabRequested = state.ContactReady = false;
        state.UltimateAttack = false;
    }

    private void ResolvePairContacts(FighterInput inputOne, FighterInput inputTwo)
    {
        Contact first = PendingContact(playerOne, playerTwo), second = PendingContact(playerTwo, playerOne);
        if (first != Contact.None && second != Contact.None)
        {
            if (first == second)
            {
                CancelContact(playerOne.State); CancelContact(playerTwo.State);
                float push = first == Contact.Heavy ? 18 : first == Contact.Grab ? 9 : 0;
                float direction = playerOne.State.X <= playerTwo.State.X ? -1 : 1;
                playerOne.State.PushVx = direction * push;
                playerTwo.State.PushVx = -direction * push;
                playerOne.State.HitStop = playerTwo.State.HitStop = first == Contact.Normal ? 6 : 12;
                return;
            }
            if (Beats(first, second)) { CancelContact(playerTwo.State); second = Contact.None; }
            else { CancelContact(playerOne.State); first = Contact.None; }
        }
        ResolveContact(playerOne, playerTwo, first, inputOne);
        ResolveContact(playerTwo, playerOne, second, inputTwo);
    }

    private void ResolveContact(Fighter fighter, Fighter opponent, Contact contact, FighterInput input)
    {
        if (contact == Contact.Grab)
        {
            TryStartGrab(fighter, opponent);
            if (fighter.State.IsGrabbing)
            {
                StepGrabOwner(fighter, opponent, input);
            }
        }
        else if (contact != Contact.None) ResolveAttackHit(fighter, opponent);
    }
}
