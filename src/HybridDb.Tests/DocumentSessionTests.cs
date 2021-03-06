﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Reflection;
using HybridDb.Commands;
using Shouldly;
using Xunit;
using System.Linq;
using HybridDb.Linq;

namespace HybridDb.Tests
{
    public class DocumentSessionTests : IDisposable
    {
        readonly DocumentStore store;

        public DocumentSessionTests()
        {
            store = DocumentStore.ForTestingWithTempTables("data source=.;Integrated Security=True;");
            //store = new DocumentStore("data source=.;Integrated Security=True;Initial Catalog=Test");
            store.Configuration.UseSerializer(new DefaultJsonSerializer());
        }

        public void Dispose()
        {
            store.Dispose();
        }

        [Fact(Skip = "Feature on holds")]
        public void CanProjectCollection()
        {
            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                var entity1 = new Entity
                {
                    Id = id,
                    Children =
                    {
                        new Entity.Child {NestedProperty = "A"},
                        new Entity.Child {NestedProperty = "B"}
                    }
                };

                session.Store(entity1);
            }
        }

        [Fact]
        public void CanEvictEntity()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                var entity1 = new Entity { Id = id, Property = "Asger" };
                session.Store(entity1);
                session.Advanced.IsLoaded(id).ShouldBe(true);
                session.Advanced.Evict(entity1);
                session.Advanced.IsLoaded(id).ShouldBe(false);
            }
        }

        [Fact]
        public void CanGetEtagForEntity()
        {
            store.Document<Entity>().MigrateSchema();

            using (var session = store.OpenSession())
            {
                var entity1 = new Entity { Id = Guid.NewGuid(), Property = "Asger" };
                session.Advanced.GetEtagFor(entity1).ShouldBe(null);

                session.Store(entity1);
                session.Advanced.GetEtagFor(entity1).ShouldBe(Guid.Empty);
                
                session.SaveChanges();
                session.Advanced.GetEtagFor(entity1).ShouldNotBe(null);
                session.Advanced.GetEtagFor(entity1).ShouldNotBe(Guid.Empty);
            }
        }

        [Fact]
        public void CanDeferCommands()
        {
            store.Document<Entity>().MigrateSchema();

            var table = store.Configuration.GetDesignFor<Entity>();
            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Advanced.Defer(new InsertCommand(table.Table, id, new{}));
                store.NumberOfRequests.ShouldBe(0);
                session.SaveChanges();
                store.NumberOfRequests.ShouldBe(1);
            }

            var entity = store.RawQuery<dynamic>(string.Format("select * from #Entities where Id = '{0}'", id)).SingleOrDefault();
            Assert.NotNull(entity);
        }

        [Fact]
        public void CanOpenSession()
        {
            store.OpenSession().ShouldNotBe(null);
        }

        [Fact]
        public void CanStoreDocument()
        {
            store.Document<Entity>().MigrateSchema();

            using (var session = store.OpenSession())
            {
                session.Store(new Entity());
                session.SaveChanges();
            }

            var entity = store.RawQuery<dynamic>("select * from #Entities").SingleOrDefault();
            Assert.NotNull(entity);
            Assert.NotNull(entity.Document);
            Assert.NotEqual(0, entity.Document.Length);
        }

        [Fact]
        public void AccessToNestedPropertyCanBeNullSafe()
        {
            store.Document<Entity>().Project(x => x.TheChild.NestedProperty).MigrateSchema();

            using (var session = store.OpenSession())
            {
                session.Store(new Entity
                {
                    TheChild = null
                });
                session.SaveChanges();
            }

            var entity = store.RawQuery<dynamic>("select * from #Entities").SingleOrDefault();
            Assert.Null(entity.TheChildNestedProperty);
        }

        [Fact]
        public void AccessToNestedPropertyThrowsIfNotMadeNullSafe()
        {
            store.Document<Entity>().Project(x => x.TheChild.NestedProperty, makeNullSafe: false).MigrateSchema();

            using (var session = store.OpenSession())
            {
                session.Store(new Entity
                {
                    TheChild = null
                });
                Should.Throw<TargetInvocationException>(() => session.SaveChanges());
            }
        }

        [Fact]
        public void StoresIdRetrievedFromObject()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id });
                session.SaveChanges();
            }

            var retrivedId = store.RawQuery<Guid>("select Id from #Entities").SingleOrDefault();
            retrivedId.ShouldBe(id);
        }

        [Fact]
        public void StoringDocumentWithSameIdTwiceIsIgnored()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                var entity1 = new Entity {Id = id, Property = "Asger"};
                var entity2 = new Entity {Id = id, Property = "Asger"};
                session.Store(entity1);
                session.Store(entity2);
                session.Load<Entity>(id).ShouldBe(entity1);
            }
        }

        [Fact]
        public void SessionWillNotSaveWhenSaveChangesIsNotCalled()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                var entity = new Entity {Id = id, Property = "Asger"};
                session.Store(entity);
                session.SaveChanges();
                entity.Property = "Lars";
                session.Advanced.Clear();
                session.Load<Entity>(id).Property.ShouldBe("Asger");
            }
        }

        [Fact]
        public void CanSaveChangesWithPessimisticConcurrency()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session1 = store.OpenSession())
            using (var session2 = store.OpenSession())
            {
                var entityFromSession1 = new Entity { Id = id, Property = "Asger" };
                session1.Store(entityFromSession1);
                session1.SaveChanges();

                var entityFromSession2 = session2.Load<Entity>(id);
                entityFromSession2.Property = " er craazy";
                session2.SaveChanges();

                entityFromSession1.Property += " er 4 real";
                session1.Advanced.SaveChangesLastWriterWins();

                session1.Advanced.Clear();
                session1.Load<Entity>(id).Property.ShouldBe("Asger er 4 real");
            }
        }

        [Fact]
        public void CanDeleteDocument()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger" });
                session.SaveChanges();
                session.Advanced.Clear();

                var entity = session.Load<Entity>(id);
                session.Delete(entity);
                session.SaveChanges();
                session.Advanced.Clear();

                session.Load<Entity>(id).ShouldBe(null);
            }
        }

        [Fact]
        public void DeletingATransientEntityRemovesItFromSession()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                var entity = new Entity {Id = id, Property = "Asger"};
                session.Store(entity);
                session.Delete(entity);
                session.Advanced.IsLoaded(id).ShouldBe(false);
            }
        }

        [Fact]
        public void DeletingAnEntityNotLoadedInSessionIsIgnored()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                var entity = new Entity { Id = id, Property = "Asger" };
                Should.NotThrow(() => session.Delete(entity));
                session.Advanced.IsLoaded(id).ShouldBe(false);
            }
        }

        [Fact]
        public void LoadingADocumentMarkedForDeletionReturnNothing()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger" });
                session.SaveChanges();
                session.Advanced.Clear();
                
                var entity = session.Load<Entity>(id);
                session.Delete(entity);
                session.Load<Entity>(id).ShouldBe(null);
            }
        }

        [Fact]
        public void LoadingAManagedDocumentWithWrongTypeReturnsNothing()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger" });
                var otherEntity = session.Load<OtherEntity>(id);
                otherEntity.ShouldBe(null);
            }
        }

        [Fact]
        public void CanClearSession()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger" });
                session.Advanced.Clear();
                session.Advanced.IsLoaded(id).ShouldBe(false);
            }
        }

        [Fact]
        public void CanCheckIfEntityIsLoadedInSession()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Advanced.IsLoaded(id).ShouldBe(false);
                session.Store(new Entity { Id = id, Property = "Asger" });
                session.Advanced.IsLoaded(id).ShouldBe(true);
            }
        }

        [Fact]
        public void CanLoadDocument()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger" });
                session.SaveChanges();
                session.Advanced.Clear();

                var entity = session.Load<Entity>(id);
                entity.Property.ShouldBe("Asger");
            }
        }

        [Fact]
        public void LoadingSameDocumentTwiceInSameSessionReturnsSameInstance()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger" });
                session.SaveChanges();
                session.Advanced.Clear();

                var document1 = session.Load<Entity>(id);
                var document2 = session.Load<Entity>(id);
                document1.ShouldBe(document2);
            }
        }

        [Fact]
        public void CanLoadATransientEntity()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                var entity = new Entity {Id = id, Property = "Asger"};
                session.Store(entity);
                session.Load<Entity>(id).ShouldBe(entity);
            }
        }

        [Fact]
        public void LoadingANonExistingDocumentReturnsNull()
        {
            store.Document<Entity>().MigrateSchema();

            using (var session = store.OpenSession())
            {
                session.Load<Entity>(Guid.NewGuid()).ShouldBe(null);
            }
        }

        [Fact]
        public void SavesChangesWhenObjectHasChanged()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger" });
                session.SaveChanges(); // 1
                session.Advanced.Clear();

                var document = session.Load<Entity>(id); // 2
                document.Property = "Lars";
                session.SaveChanges(); // 3

                store.NumberOfRequests.ShouldBe(3);
                session.Advanced.Clear();
                session.Load<Entity>(id).Property.ShouldBe("Lars");
            }
        }

        [Fact]
        public void DoesNotSaveChangesWhenObjectHasNotChanged()
        {
            store.Document<Entity>().MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger" });
                session.SaveChanges(); // 1
                session.Advanced.Clear();

                session.Load<Entity>(id); // 2
                session.SaveChanges(); // Should not issue a request

                store.NumberOfRequests.ShouldBe(2);
            }
        }

        [Fact]
        public void BatchesMultipleChanges()
        {
            store.Document<Entity>().MigrateSchema();

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();

            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id1, Property = "Asger" });
                session.Store(new Entity { Id = id2, Property = "Asger" });
                session.SaveChanges(); // 1
                store.NumberOfRequests.ShouldBe(1);
                session.Advanced.Clear();

                var entity1 = session.Load<Entity>(id1); // 2
                var entity2 = session.Load<Entity>(id2); // 3
                store.NumberOfRequests.ShouldBe(3);

                session.Delete(entity1);
                entity2.Property = "Lars";
                session.Store(new Entity { Id = id3, Property = "Jacob" });
                session.SaveChanges(); // 4
                store.NumberOfRequests.ShouldBe(4);
            }
        }

        [Fact]
        public void SavingChangesTwiceInARowDoesNothing()
        {
            store.Document<Entity>().MigrateSchema();

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();

            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id1, Property = "Asger" });
                session.Store(new Entity { Id = id2, Property = "Asger" });
                session.SaveChanges();
                session.Advanced.Clear();

                var entity1 = session.Load<Entity>(id1);
                var entity2 = session.Load<Entity>(id2);
                session.Delete(entity1);
                entity2.Property = "Lars";
                session.Store(new Entity { Id = id3, Property = "Jacob" });
                session.SaveChanges();

                var numberOfRequests = store.NumberOfRequests;
                var lastEtag = store.LastWrittenEtag;

                session.SaveChanges();

                store.NumberOfRequests.ShouldBe(numberOfRequests);
                store.LastWrittenEtag.ShouldBe(lastEtag);
            }
        }

        [Fact]
        public void IssuesConcurrencyExceptionWhenTwoSessionsChangeSameDocument()
        {
            store.Document<Entity>().MigrateSchema();

            var id1 = Guid.NewGuid();

            using (var session1 = store.OpenSession())
            using (var session2 = store.OpenSession())
            {
                session1.Store(new Entity { Id = id1, Property = "Asger" });
                session1.SaveChanges();

                var entity1 = session1.Load<Entity>(id1);
                var entity2 = session2.Load<Entity>(id1);

                entity1.Property = "A";
                session1.SaveChanges();

                entity2.Property = "B";
                Should.Throw<ConcurrencyException>(() => session2.SaveChanges());
            }
        }

        [Fact]
        public void CanQueryDocument()
        {
            store.Document<Entity>().Project(x => x.ProjectedProperty).MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger", ProjectedProperty = "Large"});
                session.Store(new Entity { Id = id, Property = "Lars", ProjectedProperty = "Small" });
                session.SaveChanges();
                session.Advanced.Clear();

                var entities = session.Query<Entity>().Where(x => x.ProjectedProperty == "Large").ToList();
                
                entities.Count.ShouldBe(1);
                entities[0].Property.ShouldBe("Asger");
                entities[0].ProjectedProperty.ShouldBe("Large");
            }
        }

        [Fact]
        public void CanSaveChangesOnAQueriedDocument()
        {
            store.Document<Entity>().Project(x => x.ProjectedProperty).MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger", ProjectedProperty = "Large"});
                session.SaveChanges();
                session.Advanced.Clear();

                var entity = session.Query<Entity>().Single(x => x.ProjectedProperty == "Large");

                entity.Property = "Lars";
                session.SaveChanges();
                session.Advanced.Clear();

                session.Load<Entity>(id).Property.ShouldBe("Lars");
            }
        }

        [Fact]
        public void QueryingALoadedDocumentReturnsSameInstance()
        {
            store.Document<Entity>().Project(x => x.ProjectedProperty).MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger", ProjectedProperty = "Large"});
                session.SaveChanges();
                session.Advanced.Clear();

                var instance1 = session.Load<Entity>(id);
                var instance2 = session.Query<Entity>().Single(x => x.ProjectedProperty == "Large");

                instance1.ShouldBe(instance2);
            }
        }

        [Fact]
        public void QueryingALoadedDocumentMarkedForDeletionReturnsNothing()
        {
            store.Document<Entity>().Project(x => x.ProjectedProperty).MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger", ProjectedProperty = "Large"});
                session.SaveChanges();
                session.Advanced.Clear();

                var entity = session.Load<Entity>(id);
                session.Delete(entity);

                var entities = session.Query<Entity>().Where(x => x.ProjectedProperty == "Large").ToList();

                entities.Count.ShouldBe(0);
            }
        }

        [Fact]
        public void CanQueryAndReturnProjection()
        {
            store.Document<Entity>().Project(x => x.ProjectedProperty).Project(x => x.TheChild.NestedProperty).MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Property = "Asger", ProjectedProperty = "Large", TheChild = new Entity.Child { NestedProperty = "Hans" } });
                session.Store(new Entity { Id = id, Property = "Lars", ProjectedProperty = "Small", TheChild = new Entity.Child { NestedProperty = "Peter" } });
                session.SaveChanges();
                session.Advanced.Clear();

                var entities = session.Query<Entity>().Where(x => x.ProjectedProperty == "Large").AsProjection<EntityProjection>().ToList();

                entities.Count.ShouldBe(1);
                entities[0].ProjectedProperty.ShouldBe("Large");
                entities[0].TheChildNestedProperty.ShouldBe("Hans");
            }
        }

        [Fact]
        public void WillNotSaveChangesToProjectedValues()
        {
            store.Document<Entity>()
                .Project(x => x.ProjectedProperty)
                .Project(x => x.TheChild.NestedProperty)
                .MigrateSchema();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity {Id = id, Property = "Asger", ProjectedProperty = "Large"});
                session.SaveChanges();
                session.Advanced.Clear();

                var entity = session.Query<Entity>().AsProjection<EntityProjection>().Single(x => x.ProjectedProperty == "Large");
                entity.ProjectedProperty = "Small";
                session.SaveChanges();
                session.Advanced.Clear();

                session.Load<Entity>(id).ProjectedProperty.ShouldBe("Large");
            }
        }

        [Fact]
        public void SavesTableReferenceToIndex()
        {
            store.Document<Entity>().Index<OtherEntityIndex>();
            store.Document<OtherEntity>().Index<OtherEntityIndex>();
            store.MigrateSchemaToMatchConfiguration();

            var id = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Number = 1 });
                session.Store(new OtherEntity { Id = id2, Number = 2 });
                session.SaveChanges();
                session.Advanced.Clear();

                var entities = store.RawQuery<dynamic>("select Id, TableReference from #OtherEntityIndex order by Number").ToList();
                Assert.Equal(id, entities[0].Id);
                Assert.Equal("Entities", entities[0].TableReference);
                Assert.Equal(id2, entities[1].Id);
                Assert.Equal("OtherEntities", entities[1].TableReference);
            }
        }

        [Fact]
        public void InsertsInIndexes()
        {
            store.Document<Entity>().Index<EntityIndex>().Index<OtherEntityIndex>();
            store.MigrateSchemaToMatchConfiguration();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Number = 1, Property = "Asger" });
                session.SaveChanges();

                var index1 = store.RawQuery<dynamic>("select * from #EntityIndex").ToList();
                Assert.Equal(id, index1[0].Id);
                Assert.Equal("Asger", index1[0].Property);

                var index2 = store.RawQuery<dynamic>("select * from #OtherEntityIndex").ToList();
                Assert.Equal(id, index2[0].Id);
                Assert.Equal(1, index2[0].Number);
            }
        }

        [Fact]
        public void UpdatesIndexes()
        {
            store.Document<Entity>().Index<EntityIndex>().Index<OtherEntityIndex>();
            store.MigrateSchemaToMatchConfiguration();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Number = 1, Property = "Asger" });
                session.SaveChanges();
                session.Advanced.Clear();

                var entity = session.Load<Entity>(id);
                entity.Property = "Lars";
                entity.Number = 2;
                session.SaveChanges();

                var index1 = store.RawQuery<dynamic>("select * from #EntityIndex").ToList();
                Assert.Equal(id, index1[0].Id);
                Assert.Equal("Lars", index1[0].Property);

                var index2 = store.RawQuery<dynamic>("select * from #OtherEntityIndex").ToList();
                Assert.Equal(id, index2[0].Id);
                Assert.Equal(2, index2[0].Number);
            }
        }

        [Fact]
        public void DeletesFromIndexes()
        {
            store.Document<Entity>().Index<EntityIndex>().Index<OtherEntityIndex>();
            store.MigrateSchemaToMatchConfiguration();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new Entity { Id = id, Number = 1, Property = "Asger" });
                session.SaveChanges();
                session.Advanced.Clear();

                var entity = session.Load<Entity>(id);
                session.Delete(entity);
                session.SaveChanges();

                var index1 = store.RawQuery<dynamic>("select * from #EntityIndex").ToList();
                index1.Count.ShouldBe(0);

                var index2 = store.RawQuery<dynamic>("select * from #OtherEntityIndex").ToList();
                index2.Count.ShouldBe(0);
            }
        }

        [Fact]
        public void CanLoadByBestMatchingIndex()
        {
            store.Document<MoreDerivedEntity1>().Index<EntityIndex>();
            store.Document<MoreDerivedEntity2>().Index<EntityIndex>();
            store.MigrateSchemaToMatchConfiguration();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new MoreDerivedEntity1 { Id = id });
                session.SaveChanges();
                session.Advanced.Clear();

                var entity = session.Load<AbstractEntity>(id);
                entity.ShouldBeTypeOf<MoreDerivedEntity1>();
            }
        }

        [Fact]
        public void LoadCanReturnNullFromIndex()
        {
            store.Document<MoreDerivedEntity1>().Index<EntityIndex>();
            store.Document<MoreDerivedEntity2>().Index<EntityIndex>();
            store.MigrateSchemaToMatchConfiguration();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                var entity = session.Load<AbstractEntity>(id);
                entity.ShouldBe(null);
            }
        }
        
        [Fact]
        public void CanQueryIndex()
        {
            store.Document<MoreDerivedEntity1>().Index<EntityIndex>();
            store.Document<MoreDerivedEntity2>().Index<EntityIndex>();
            store.MigrateSchemaToMatchConfiguration();

            using (var session = store.OpenSession())
            {
                session.Store(new MoreDerivedEntity1 { Id = Guid.NewGuid(), Property = "Asger" });
                session.Store(new MoreDerivedEntity2 { Id = Guid.NewGuid(), Property = "Asger" });
                session.SaveChanges();
                session.Advanced.Clear();

                var entities = session.Query<EntityIndex>().Where(x => x.Property == "Asger").ToList();
                entities.Count().ShouldBe(2);
            }
        }

        [Fact]
        public void FailsWhenNonNullableIndexPropertyNameIsNotPresentOnDocument()
        {
            store.Document<OtherEntity>().Index<BadMatchIndex>();
            store.MigrateSchemaToMatchConfiguration();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new OtherEntity { Id = id, Number = 1 });
                Should.Throw<SqlException>(() => session.SaveChanges());
            }
        }

        [Fact]
        public void DoesNotFailWhenIndexPropertyNameIsNotPresentOnDocumentButHasOverride()
        {
            store.Document<OtherEntity>().Index<BadMatchIndex>().With(x => x.BadMatch, x => 10);
            store.MigrateSchemaToMatchConfiguration();

            var id = Guid.NewGuid();
            using (var session = store.OpenSession())
            {
                session.Store(new OtherEntity { Id = id, Number = 1 });
                session.SaveChanges();

                var index1 = store.RawQuery<long>("select BadMatch from #BadMatchIndex").Single();
                index1.ShouldBe(10);
            }
        }

        public class Entity
        {
            public Entity()
            {
                TheChild = new Child();
                TheSecondChild = new Child();
                Children = new List<Child>();
            }

            public Guid Id { get; set; }
            public string ProjectedProperty { get; set; }
            public List<Child> Children { get; set; }
            public string Property { get; set; }
            public int Number { get; set; }
            public Child TheChild { get; set; }
            public Child TheSecondChild { get; set; }
            
            public class Child
            {
                public string NestedProperty { get; set; }
            }
        }

        public class OtherEntity
        {
            public Guid Id { get; set; }
            public int Number { get; set; }
        }

        public abstract class AbstractEntity
        {
            public Guid Id { get; set; }
            public string Property { get; set; }
            public int Number { get; set; }
        }

        public class DerivedEntity : AbstractEntity { }
        public class MoreDerivedEntity1 : DerivedEntity { }
        public class MoreDerivedEntity2 : DerivedEntity { }

        public class EntityProjection
        {
            public string ProjectedProperty { get; set; }
            public string TheChildNestedProperty { get; set; }
        }

        public class EntityIndex
        {
            public string Property { get; set; }
        }

        public class OtherEntityIndex
        {
            public int Number { get; set; }
        }

        public class BadMatchIndex
        {
            public long BadMatch { get; set; }
        }
    }
}