namespace Arcvora.Core.Model;

/// <summary>Bilginin nereden geldiği. Onay durumundan ayrı tutulur (§14.2).</summary>
public enum Origin { UserStatement, SystemProposal, Assumption }

/// <summary>İnsan inceleme durumu. Kabul edilse bile <see cref="Origin"/> kaybolmaz.</summary>
public enum ReviewStatus { Proposed, Confirmed, Rejected, Deferred }

public enum RequirementKind { Functional, Quality, Constraint, ExistingDecision }

public enum Priority { Critical, High, Medium, Low }

public enum ComponentKind { Client, Api, Module, Worker, Database, ExternalService, Queue, Storage }

public enum CommunicationKind { Sync, Async, Data, Event }

public enum AssumptionStatus { Unverified, Verified, Invalidated }

public enum QuestionStatus { Open, Answered, Deferred }

/// <summary>İzlenebilirlik bağlantı türleri.</summary>
public enum TraceKind
{
    /// <summary>Decision → Requirement</summary>
    Addresses,
    /// <summary>Component | Entity | Flow | Endpoint | Relationship → Decision</summary>
    Implements,
    /// <summary>Decision → Decision</summary>
    DependsOn,
    /// <summary>Decision → Assumption</summary>
    Assumes,
    /// <summary>Herhangi bir nesne → OpenQuestion (cevap bekleyen belirsizlik)</summary>
    BlockedBy,
}

public enum StepKind { Call, Async, Return }

public enum Cardinality { OneToOne, OneToMany, ManyToOne, ManyToMany, ZeroOrOneToMany }

public enum ObjectType
{
    Source, Requirement, Assumption, Decision, Component, Relationship,
    Entity, Actor, Flow, Endpoint, TraceLink, OpenQuestion,
}
