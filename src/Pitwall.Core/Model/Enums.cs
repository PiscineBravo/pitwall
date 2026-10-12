namespace Pitwall.Model
{
    /// <summary>Every session type a Formula-style race weekend can contain.</summary>
    public enum SessionKind
    {
        Practice1,
        Practice2,
        Practice3,
        Qualifying,
        SprintQualifying,
        Sprint,
        Race
    }

    /// <summary>How the length of a session is expressed.</summary>
    public enum SessionLengthMode
    {
        /// <summary>Fixed number of laps.</summary>
        Laps,
        /// <summary>Fixed duration in minutes, the usual choice for practice and qualifying.</summary>
        Minutes,
        /// <summary>Percentage of the real grand prix distance: 25, 50, 100 ...</summary>
        DistancePercent
    }

    /// <summary>Weekend layout: the classic three-practice weekend or the sprint weekend.</summary>
    public enum WeekendFormat
    {
        Standard,
        Sprint
    }

    /// <summary>Qualifying layout.</summary>
    public enum QualifyingFormat
    {
        /// <summary>One single open session, best lap counts.</summary>
        Single,
        /// <summary>Q1 / Q2 / Q3 with elimination between segments.</summary>
        Knockout,
        /// <summary>One flying lap per driver, slowest first.</summary>
        OneShot
    }

    /// <summary>Progress of a round inside the championship.</summary>
    public enum RoundStatus
    {
        Scheduled,
        InProgress,
        Completed,
        Cancelled
    }

    /// <summary>How a classified entry ended its race.</summary>
    public enum FinishStatus
    {
        Finished,
        /// <summary>Running at the flag but at least one lap down; still classified.</summary>
        Lapped,
        Retired,
        Disqualified,
        DidNotStart,
        NotClassified
    }

    /// <summary>Driving aid level enforced by the championship regulations.</summary>
    public enum AssistLevel
    {
        Forbidden,
        Limited,
        Free
    }
}
