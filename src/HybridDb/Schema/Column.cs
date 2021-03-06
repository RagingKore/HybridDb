﻿using System;

namespace HybridDb.Schema
{
    public class Column
    {
        public Column(string name, SqlColumn sqlColumn)
        {
            Name = name;
            SqlColumn = sqlColumn;
        }

        public Column(string name, Type type)
        {
            Name = name;
            SqlColumn = type != null ? new SqlColumn(type) : new SqlColumn();
        }

        public string Name { get; protected set; }
        public SqlColumn SqlColumn { get; protected set; }

        protected bool Equals(Column other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Name, other.Name);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Column);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        public static bool operator ==(Column left, Column right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Column left, Column right)
        {
            return !Equals(left, right);
        }

        public static implicit operator string(Column self)
        {
            return self.Name;
        }
    }
}