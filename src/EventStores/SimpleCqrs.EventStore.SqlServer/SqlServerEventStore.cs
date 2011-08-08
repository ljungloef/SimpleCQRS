﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using ServiceStack.Text;
using SimpleCqrs.Eventing;

namespace SimpleCqrs.EventStore.SqlServer
{
    public class SqlServerEventStore : IEventStore
    {
        private readonly IDomainEventSerializer serializer;
        private readonly SqlServerConfiguration configuration;
        private Dictionary<string, Type> domainEventTypesDictionary;

        public SqlServerEventStore(SqlServerConfiguration configuration, IDomainEventSerializer serializer)
        {
            this.serializer = serializer;
            this.configuration = configuration;
            Init();
        }

        public bool UseShortEventTypeNames { get; set; }

        public void Init()
        {
            using (var connection = new SqlConnection(configuration.ConnectionString))
            {
                connection.Open();
                var sql = string.Format(SqlStatements.CreateTheEventStoreTable, "EventStore");
                using (var command = new SqlCommand(sql, connection))
                    command.ExecuteNonQuery();
                connection.Close();
            }

            var list = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var domainEventTypes = assembly.GetTypes().Where(x => x.BaseType == typeof (DomainEvent));
                list.AddRange(domainEventTypes);
            }
            domainEventTypesDictionary = list.ToDictionary(x => x.Name, x => x);
        }

        public IEnumerable<DomainEvent> GetEvents(Guid aggregateRootId, int startSequence)
        {
            var events = new List<DomainEvent>();
            using (var connection = new SqlConnection(configuration.ConnectionString))
            {
                connection.Open();
                var sql = string.Format(SqlStatements.GetEventsByAggregateRootAndSequence, "", "EventStore", aggregateRootId,
                                        startSequence);
                using (var command = new SqlCommand(sql, connection))
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        var type = reader["EventType"].ToString();
                        var data = reader["data"].ToString();

                        var targetType = GetTheTargetType(type);
                        events.Add(serializer.Deserialize(targetType, data));
                    }
                connection.Close();
            }
            return events;
        }

        private Type GetTheTargetType(string type)
        {
            if (type.Contains(","))
                return Type.GetType(type);


            return domainEventTypesDictionary[type];
        }

        public void Insert(IEnumerable<DomainEvent> domainEvents)
        {
            var sql = new StringBuilder();
            foreach (var domainEvent in domainEvents)
            {
                var type = GetTheEventType(domainEvent);
                sql.AppendFormat(SqlStatements.InsertEvents, "EventStore", type, domainEvent.AggregateRootId, domainEvent.EventDate, domainEvent.Sequence,
                                 (serializer.Serialize(domainEvent) ?? string.Empty)
                                     .Replace("'", "''"));
            }

            if (sql.Length <= 0) return;

            using (var connection = new SqlConnection(configuration.ConnectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(sql.ToString(), connection))
                    command.ExecuteNonQuery();
                connection.Close();
            }
        }

        private string GetTheEventType(DomainEvent domainEvent)
        {
            if (UseShortEventTypeNames)
                return domainEvent.GetType().Name;
            return TypeToStringHelperMethods.GetString(domainEvent.GetType());
        }

        public IEnumerable<DomainEvent> GetEventsByEventTypes(IEnumerable<Type> domainEventTypes, DateTime startDate, DateTime endDate)
        {
            var events = new List<DomainEvent>();

            var eventParameters = domainEventTypes.Select(TypeToStringHelperMethods.GetString).Join("','");

            using (var connection = new SqlConnection(configuration.ConnectionString))
            {
                connection.Open();
                var sql = string.Format(SqlStatements.GetEventsByType, "EventStore", eventParameters);
                using (var command = new SqlCommand(sql, connection))
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        var type = reader["EventType"].ToString();
                        var data = reader["data"].ToString();

                        var domainEvent = serializer.Deserialize(Type.GetType(type), data);
                        events.Add(domainEvent);
                    }
                connection.Close();
            }
            return events;
        }

    }
}