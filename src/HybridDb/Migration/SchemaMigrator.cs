using System;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Transactions;
using HybridDb.Schema;
using IsolationLevel = System.Transactions.IsolationLevel;

namespace HybridDb.Migration
{
    public class SchemaMigrator : ISchemaMigrator
    {
        readonly IDocumentStore store;
        readonly TransactionScope tx;

        public SchemaMigrator(IDocumentStore store)
        {
            this.store = store;
            tx = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions {IsolationLevel = IsolationLevel.Serializable});
        }

        public ISchemaMigrator MigrateTo(Table table, bool safe = true)
        {
            var timer = Stopwatch.StartNew();

            if (!store.IsInTestMode)
            {
                var existingTables = store.RawQuery<dynamic>("select * from information_schema.tables where table_catalog = '" + table.Name + "'", null);
                if (existingTables.Any())
                    throw new InvalidOperationException("You cannot initialize a database that is not empty.");
            }

            AddTableAndColumnsAndAssociatedTables(table);

            store.Configuration.Logger.Info("HybridDb store is initialized in {0}ms", timer.ElapsedMilliseconds);
            return this;
        }

        public ISchemaMigrator AddTableAndColumnsAndAssociatedTables(Table table)
        {
            foreach (var column in table.Columns.OfType<CollectionColumn>())
            {
                AddTable(table.Name + "_" + column.Name, "Id UniqueIdentifier");
            }

            return AddTable(table.Name, table.Columns.Select(col => string.Format("{0} {1}", col.Name, GetColumnSqlType(col))).ToArray());
        }

        public ISchemaMigrator RemoveTableAndAssociatedTables(Table table)
        {
            return RemoveTable(table.Name);
        }

        public ISchemaMigrator RenameTableAndAssociatedTables(Table oldTable, string newTablename)
        {
            return RenameTable(oldTable.Name, newTablename);
        }

        public ISchemaMigrator AddColumn(string tablename, Column column)
        {
            var sql = GetColumnSqlType(column);
            return AddColumn(tablename, column.Name, sql);
        }

        public ISchemaMigrator AddTable(string tablename, params string[] columns)
        {
            var escaptedColumns =
                from column in columns
                let split = column.Split(' ')
                let name = split.First()
                let type = string.Join(" ", split.Skip(1))
                select store.Escape(name) + " " + type;

            return Execute(
                string.Format("if not ({0}) begin create table {1} ({2}); end",
                              GetTableExistsSql(tablename),
                              store.FormatTableNameAndEscape(tablename),
                              string.Join(", ", escaptedColumns)));
        }

        public ISchemaMigrator RemoveTable(string tablename)
        {
            return Execute(
                string.Format("drop table {0};", store.FormatTableNameAndEscape(tablename)));
        }

        public ISchemaMigrator RenameTable(string oldTablename, string newTablename)
        {
            if (store.IsInTestMode)
                throw new NotSupportedException("It is not possible to rename temp tables, so RenameTable is not supported when store is in test mode.");

            return Execute(
                string.Format("sp_rename {0}, {1};", store.FormatTableNameAndEscape(oldTablename), store.FormatTableNameAndEscape(newTablename)));
        }

        public ISchemaMigrator AddColumn(string tablename, string columnname, SqlBuilder columntype)
        {
            var sql = new SqlBuilder();
            sql.Append("alter table {0} add {1}", store.FormatTableNameAndEscape(tablename), store.Escape(columnname));
            sql.Append(columntype);

            return Execute(sql.ToDynamicSql(), sql.Parameters);
        }

        public ISchemaMigrator RemoveColumn(string tablename, string columnname)
        {
            return Execute(
                string.Format("alter table {0} drop column {1};", store.FormatTableNameAndEscape(tablename), store.Escape(columnname)));
        }

        public ISchemaMigrator RenameColumn(string tablename, string oldColumnname, string newColumnname)
        {
            if (store.IsInTestMode)
                throw new NotSupportedException("It is not possible to rename columns on temp tables, therefore RenameColumn is not supported when store is in test mode.");

            return Execute(
                string.Format("sp_rename '{0}.{1}', '{2}', 'COLUMN'", store.FormatTableNameAndEscape(tablename), oldColumnname, newColumnname));
        }

        public ISchemaMigrator Commit()
        {
            tx.Complete();
            return this;
        }

        public ISchemaMigrator Execute(string sql, object param = null)
        {
            store.RawExecute(sql, param);
            return this;
        }

        public void Dispose()
        {
            tx.Dispose();
        }

        SqlBuilder GetColumnSqlType(Column column)
        {
            if (column.SqlColumn.Type == null)
                throw new ArgumentException(string.Format("Column {0} must have a type", column.Name));

            var sql = new SqlBuilder();

            sql.Append(new SqlParameter {DbType = (DbType) column.SqlColumn.Type}.SqlDbType.ToString());
            sql.Append(column.SqlColumn.Length != null, "(" + (column.SqlColumn.Length == Int32.MaxValue ? "MAX" : column.SqlColumn.Length.ToString()) + ")");
            sql.Append(column.SqlColumn.Nullable, "NULL").Or("NOT NULL");
            sql.Append(column.SqlColumn.DefaultValue != null,
                       "DEFAULT @DefaultValue",
                       new Parameter
                       {
                           Name = "DefaultValue",
                           DbType = column.SqlColumn.Type,
                           Value = column.SqlColumn.DefaultValue
                       });
            sql.Append(column.SqlColumn.IsPrimaryKey, " PRIMARY KEY");

            return sql;
        }

        string GetTableExistsSql(string tablename)
        {
            return string.Format(store.IsInTestMode
                                     ? "OBJECT_ID('tempdb..{0}') is not null"
                                     : "exists (select * from information_schema.tables where table_catalog = db_name() and table_name = '{0}')",
                                 store.FormatTableName(tablename));
        }
    }
}