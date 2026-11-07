using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Classes.Mesh
{
    public class VertexData
    {
        public uint m_CurrentChannels;
        public uint m_VertexCount;
        public ChannelInfo[] m_Channels = Array.Empty<ChannelInfo>();
        public StreamInfo[] m_Streams = Array.Empty<StreamInfo>();
        public byte[] m_DataSize = Array.Empty<byte>();

        public VertexData() { }

        public VertexData(AssetTypeValueField vdField, UnityVersion version)
        {
            if (vdField.IsDummy) return;

            if (version.major < 2018)
            {
                var cur = vdField["m_CurrentChannels"];
                if (!cur.IsDummy) m_CurrentChannels = cur.AsUInt;
            }

            var vc = vdField["m_VertexCount"];
            if (!vc.IsDummy) m_VertexCount = vc.AsUInt;

            if (version.major >= 4)
            {
                var chanArray = vdField["m_Channels"]["Array"];
                if (chanArray.IsDummy) chanArray = vdField["m_Channels.Array"];

                if (!chanArray.IsDummy)
                {
                    int count = chanArray.AsArray.size;
                    m_Channels = new ChannelInfo[count];
                    for (int i = 0; i < count; i++)
                    {
                        m_Channels[i] = new ChannelInfo(chanArray[i]);
                    }
                }
            }

            if (version.major < 5)
            {
                var streamArray = vdField["m_Streams"]["Array"];
                if (streamArray.IsDummy) streamArray = vdField["m_Streams.Array"];

                if (!streamArray.IsDummy)
                {
                    int count = streamArray.AsArray.size;
                    m_Streams = new StreamInfo[count];
                    for (int i = 0; i < count; i++)
                    {
                        m_Streams[i] = new StreamInfo(streamArray[i], version);
                    }
                }

                if (version.major < 4)
                {
                    GetChannels(version);
                }
            }
            else
            {
                GetStreams(version);
            }

            m_DataSize = vdField.GetByteArraySafe("m_DataSize");
        }

        public void WriteTo(AssetTypeValueField vdField, UnityVersion version)
        {
            if (vdField.IsDummy) return;

            if (version.major < 2018)
            {
                var cur = vdField["m_CurrentChannels"];
                if (!cur.IsDummy) cur.AsUInt = m_CurrentChannels;
            }

            var vc = vdField["m_VertexCount"];
            if (!vc.IsDummy) vc.AsUInt = m_VertexCount;

            // Channels write using Children.Clear & templates
            if (version.major >= 4)
            {
                var chanArray = vdField["m_Channels.Array"];
                if (chanArray.IsDummy) chanArray = vdField["m_Channels"]["Array"];

                if (!chanArray.IsDummy)
                {
                    chanArray.Children.Clear();
                    for (int i = 0; i < m_Channels.Length; i++)
                    {
                        var slot = chanArray.GetEmptyArrayField() ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(chanArray);
                        if (slot != null && !slot.IsDummy)
                        {
                            m_Channels[i].WriteTo(slot);
                            chanArray.Children.Add(slot);
                        }
                    }
                }
            }

            // Streams write (Unity < 5.0 only)
            if (version.major < 5)
            {
                var streamArray = vdField["m_Streams.Array"];
                if (streamArray.IsDummy) streamArray = vdField["m_Streams"]["Array"];

                if (!streamArray.IsDummy)
                {
                    streamArray.Children.Clear();
                    for (int i = 0; i < m_Streams.Length; i++)
                    {
                        var slot = streamArray.GetEmptyArrayField() ?? ValueBuilder.DefaultValueFieldFromArrayTemplate(streamArray);
                        if (slot != null && !slot.IsDummy)
                        {
                            m_Streams[i].WriteTo(slot, version);
                            streamArray.Children.Add(slot);
                        }
                    }
                }
            }

            var dataField = vdField["m_DataSize"];
            if (!dataField.IsDummy)
            {
                dataField.AsByteArray = m_DataSize;
            }
        }

        public void GetStreams(UnityVersion version)
        {
            if (m_Channels.Length == 0) return;
            var streamCount = m_Channels.Max(x => x.stream) + 1;
            m_Streams = new StreamInfo[streamCount];
            uint offset = 0;

            for (int s = 0; s < streamCount; s++)
            {
                uint chnMask = 0;
                uint stride = 0;
                for (int chn = 0; chn < m_Channels.Length; chn++)
                {
                    var ch = m_Channels[chn];
                    if (ch.stream == s && ch.dimension > 0)
                    {
                        chnMask |= 1u << chn;
                        stride += (uint)(ch.dimension * MeshHelper.GetFormatSize(MeshHelper.ToVertexFormat(ch.format, version)));
                    }
                }

                m_Streams[s] = new StreamInfo
                {
                    channelMask = chnMask,
                    offset = offset,
                    stride = stride,
                    dividerOp = 0,
                    frequency = 0
                };

                offset += m_VertexCount * stride;
                offset = (offset + (16u - 1u)) & ~(16u - 1u); // 16-byte alignment
            }
        }

        private void GetChannels(UnityVersion version)
        {
            m_Channels = new ChannelInfo[6];
            for (int i = 0; i < 6; i++) m_Channels[i] = new ChannelInfo();

            for (int s = 0; s < m_Streams.Length; s++)
            {
                var st = m_Streams[s];
                var channelMask = new BitArray(new[] { (int)st.channelMask });
                byte offset = 0;

                for (int i = 0; i < 6; i++)
                {
                    if (channelMask.Get(i))
                    {
                        var ch = m_Channels[i];
                        ch.stream = (byte)s;
                        ch.offset = offset;
                        switch (i)
                        {
                            case 0:
                            case 1:
                                ch.format = 0;
                                ch.dimension = 3;
                                break;
                            case 2:
                                ch.format = 2;
                                ch.dimension = 4;
                                break;
                            case 3:
                            case 4:
                                ch.format = 0;
                                ch.dimension = 2;
                                break;
                            case 5:
                                ch.format = 0;
                                ch.dimension = 4;
                                break;
                        }
                        offset += (byte)(ch.dimension * MeshHelper.GetFormatSize(MeshHelper.ToVertexFormat(ch.format, version)));
                    }
                }
            }
        }
    }
}
