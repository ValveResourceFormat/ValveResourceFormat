using System.Windows.Forms;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace GUI.Forms
{
    partial class EntityInfoControl : UserControl
    {
        /// <summary>
        /// Raised when a property naming a file or an entity, or the entity on the other end of a
        /// connection, is double clicked.
        /// </summary>
        public event EventHandler<LinkedResource>? LinkedResourceActivated;

        public EntityInfoControl()
        {
            InitializeComponent();

            components ??= new System.ComponentModel.Container();
            components.Add(tabPageOutputs);
            components.Add(tabPageInputs);
            components.Add(tabPageReferences);

            dataGridProperties.CellDoubleClick += OnPropertiesCellDoubleClick;
            dataGridOutputs.CellDoubleClick += OnOutputsCellDoubleClick;
            dataGridInputs.CellDoubleClick += OnInputsCellDoubleClick;
            dataGridReferences.CellDoubleClick += OnReferencesCellDoubleClick;
        }

        private TabPage[] TabPageOrder => [tabPageProperties, tabPageOutputs, tabPageInputs, tabPageReferences];

        public void ShowPopulatedTabs()
        {
            SetTabVisible(tabPageOutputs, dataGridOutputs.RowCount > 0);
            SetTabVisible(tabPageInputs, dataGridInputs.RowCount > 0);
            SetTabVisible(tabPageReferences, dataGridReferences.RowCount > 0);
        }

        private void SetTabVisible(TabPage page, bool shouldShow)
        {
            var isShown = tabControl.TabPages.Contains(page);

            if (shouldShow && !isShown)
            {
                tabControl.TabPages.Insert(GetInsertIndex(page), page);
            }
            else if (!shouldShow && isShown)
            {
                tabControl.TabPages.Remove(page);
            }
        }

        private int GetInsertIndex(TabPage page)
        {
            var targetOrder = Array.IndexOf(TabPageOrder, page);
            var index = 0;

            for (var i = 0; i < targetOrder; i++)
            {
                if (tabControl.TabPages.Contains(TabPageOrder[i]))
                {
                    index++;
                }
            }

            return index;
        }

        public void Clear()
        {
            dataGridProperties.Rows.Clear();
            dataGridOutputs.Rows.Clear();
            dataGridInputs.Rows.Clear();
            dataGridReferences.Rows.Clear();
            tabControl.SelectedTab = tabPageProperties;
        }

        /// <summary>
        /// Shows an entity's properties, connections and references. <paramref name="entities"/> is the
        /// world the entity is in, searched for the entities its properties name, the entities whose
        /// properties name it, the connections targeting it and the targets of its own.
        /// </summary>
        public void PopulateFromEntity(List<Entity> entities, Entity entity)
        {
            foreach (var child in entity.Children)
            {
                var text = BareText(child.Value);
                LinkedResource? link = text switch
                {
                    null or "" => null,
                    _ when FindEntity(entities, text) is { } namedEntity => new LinkedEntity(namedEntity),
                    _ => new LinkedFile(text),
                };

                AddProperty(child.Key, text ?? StringifyValue(child.Value), link);
            }

            if (entity.Connections != null)
            {
                foreach (var connection in entity.Connections)
                {
                    AddOutputConnection(connection, FindEntity(entities, connection.TargetName));
                }
            }

            foreach (var connection in entity.GetInputConnections(entities))
            {
                AddInputConnection(connection);
            }

            AddReferences(entities, entity);
        }

        /// <summary>
        /// Lists the properties of other entities that name this one
        /// </summary>
        private void AddReferences(List<Entity> entities, Entity entity)
        {
            if (string.IsNullOrEmpty(entity.TargetName))
            {
                return;
            }

            foreach (var source in entities)
            {
                if (source == entity)
                {
                    continue;
                }

                foreach (var child in source.Children)
                {
                    if (child.Key == "targetname")
                    {
                        continue;
                    }

                    var text = BareText(child.Value);

                    if (string.IsNullOrEmpty(text) || !EntityNameMatches(text, entity.TargetName))
                    {
                        continue;
                    }

                    var rowIndex = dataGridReferences.Rows.Add([
                        source.TargetName ?? "",
                        source.GetStringProperty("classname", string.Empty),
                        child.Key,
                        text,
                    ]);

                    dataGridReferences.Rows[rowIndex].Tag = new LinkedEntity(source);
                }
            }
        }

        private static Entity? FindEntity(List<Entity> entities, string targetPattern)
        {
            return entities.Find(candidate => candidate.TargetName is { } targetName && EntityNameMatches(targetPattern, targetName));
        }

        public void AddProperty(string name, string value, LinkedResource? link = null)
        {
            var rowIndex = dataGridProperties.Rows.Add([name, value]);
            dataGridProperties.Rows[rowIndex].Tag = link;
        }

        /// <summary>
        /// The bare text of a string property. The KV3 form a value serializes to carries its quotes
        /// and, for a resource, its type prefix (<c>resource_name:"particles/foo.vpcf"</c>), which is
        /// neither what the grid should show nor a path anything can be looked up by.
        /// </summary>
        private static string? BareText(KVObject value)
            => value.ValueType == KVValueType.String ? (string)value : null;

        private void AddOutputConnection(Connection connectionData, Entity? target)
        {
            var rowIndex = dataGridOutputs.Rows.Add([
                connectionData.OutputName,
                connectionData.TargetName,
                connectionData.InputName,
                connectionData.OverrideParam,
                connectionData.Delay,
                GetStringTimesToFire(connectionData.TimesToFire)
            ]);

            dataGridOutputs.Rows[rowIndex].Tag = target != null ? new LinkedEntity(target) : null;
        }

        private void AddInputConnection(Connection connectionData)
        {
            var rowIndex = dataGridInputs.Rows.Add([
                connectionData.SourceEntity.TargetName ?? "",
                connectionData.OutputName,
                connectionData.InputName,
                connectionData.OverrideParam,
                connectionData.Delay,
                GetStringTimesToFire(connectionData.TimesToFire)
            ]);

            dataGridInputs.Rows[rowIndex].Tag = new LinkedEntity(connectionData.SourceEntity);
        }

        private static string GetStringTimesToFire(int timesToFire)
        {
            return timesToFire switch
            {
                1 => "Only Once",
                >= 2 => $"Only {timesToFire} Times",
                _ => "Infinite",
            };
        }

        private void OnPropertiesCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            ShowLinkedResource(dataGridProperties, ColumnValue, e);
        }

        private void OnOutputsCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            ShowLinkedResource(dataGridOutputs, OutputsTargetEntity, e);
        }

        private void OnInputsCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            ShowLinkedResource(dataGridInputs, InputsSource, e);
        }

        private void OnReferencesCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            ShowLinkedResource(dataGridReferences, ReferencesSource, e);
        }

        private void ShowLinkedResource(DataGridView grid, DataGridViewColumn linkColumn, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != linkColumn.Index || grid.Rows[e.RowIndex].Tag is not LinkedResource link)
            {
                return;
            }

            LinkedResourceActivated?.Invoke(this, link);
        }
    }
}
