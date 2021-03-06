﻿using System.Linq;

namespace HybridDb.Tests
{
    public static class DocumentStoreEx
    {
        public static bool TableExists(this DocumentStore store, string name)
        {
            if (store.IsInTestMode)
            {
                return store.RawQuery<dynamic>(string.Format("select OBJECT_ID('tempdb..{0}') as Result", store.FormatTableName(name))).First().Result != null;
            }

            return store.RawQuery<dynamic>(string.Format("select OBJECT_ID('{0}') as Result", name)).First().Result != null;
        }

        public static Column GetColumn(this DocumentStore store, string table, string column)
        {
            if (store.IsInTestMode)
            {
                return store
                    .RawQuery<Column>(string.Format("select * from tempdb.sys.columns where Name = N'{0}' and Object_ID = Object_ID(N'tempdb..{1}')",
                                                    column,
                                                    store.FormatTableName(table)))
                    .FirstOrDefault();
            }

            return store
                .RawQuery<Column>(string.Format("select * from master.sys.columns where Name = N'{0}' and Object_ID = Object_ID(N'{1}')", column, table))
                .FirstOrDefault();
        }

        public static string GetType(this DocumentStore store, int id)
        {
            return store.RawQuery<string>("select name from sys.types where system_type_id = @id", new { id }).FirstOrDefault();
        }

        public class Column
        {
            public string Name { get; set; }
            public int system_type_id { get; set; }
            public int max_length { get; set; }
        }
    }
}