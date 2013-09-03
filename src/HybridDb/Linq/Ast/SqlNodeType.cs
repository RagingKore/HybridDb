﻿namespace HybridDb.Linq.Ast
{
    public enum SqlNodeType
    {
        Query,
        Select,
        Where,
        Not,
        And,
        Or,
        Equal,
        NotEqual,
        LikeStartsWith,
        LikeEndsWith,
        LikeContains,
        In,
        Is,
        IsNot,
        Constant,
        Column,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        BitwiseAnd,
        BitwiseOr,
        Project,
        Ordering,
        OrderBy
    }
}