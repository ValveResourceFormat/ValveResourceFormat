using System.ComponentModel;

namespace NodeGraphControl {
    partial class NodeGraphControl {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }

            _gridPen.Dispose();
            _gridBrush.Dispose();
            transformation.Dispose();
            inverse_transformation.Dispose();

            // foreach (var node in _graphNodes) {
            //     node.Dispose();
            // }

            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            components = new System.ComponentModel.Container();
        }

        #endregion
    }
}