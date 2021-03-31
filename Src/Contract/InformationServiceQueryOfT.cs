using System;
using System.Collections.Generic;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace SolarWinds.InformationService.Contract2
{
    public class InformationServiceQuery<T> : InformationServiceQuery, IEnumerable<T> where T : new()
    {
        private readonly Type type = typeof(T);
        private readonly IResponseParser<T> parser;

        public InformationServiceQuery(ILogger<InformationServiceQuery<T>> logger, InformationServiceContext context, string queryString)
            : this(logger, context, queryString, null)
        {
        }

        public InformationServiceQuery(ILogger<InformationServiceQuery<T>> logger, InformationServiceContext context, string query, PropertyBag parameters)
            : base(logger, context, query, parameters)
        {
            object[] attributes = type.GetCustomAttributes(typeof(InformationServiceEntityAttribute), false);

            if (attributes.Length > 0)
            {
                InformationServiceEntityAttribute entityAttribute = attributes[0] as InformationServiceEntityAttribute;
                parser = entityAttribute != null ? ChooseResponseParser(entityAttribute.ParserType) : new ResponseParser<T>();
            }
            else
            {
                parser = new ResponseParser<T>();
            }
        }

        private IResponseParser<T> ChooseResponseParser(ParserType parserInstance)
        {
            switch (parserInstance)
            {
                case ParserType.EntityCollectionResponseParser:
                    return new EntityCollectionResponseParser<T>();
                default:
                    return new ResponseParser<T>();
            }
        }

        #region IEnumerable<T> Members

        public virtual IEnumerator<T> GetEnumerator()
        {
            XmlReader reader = Execute();

            T instance;

            do
            {
                instance = parser.ReadNextEntity(reader);
                if (instance != null)
                    yield return instance;
            }
            while (instance != null);
        }

        #endregion

        #region IEnumerable Members

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    }
}
