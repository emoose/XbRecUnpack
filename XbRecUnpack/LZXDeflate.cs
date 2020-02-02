/* LZXDeflate from Microsoft.XboxLive.Avatars.dll
 * With fixes to prevent out-of-bounds accesses by emoose
 * (~30% faster than LzxDecoder impl) */

using System;

namespace XbRecUnpack
{
	public class LZXDeflate
	{
		private readonly int dec_window_size;
		private readonly int dec_window_mask;

		private byte[] dec_mem_window;
		private byte[] dec_extra_bits;
		private int[] MP_POS_minus2;

		private byte[] dec_main_tree_len;
		private byte[] dec_main_tree_prev_len;
		private byte[] dec_secondary_length_tree_len;
		private byte[] dec_secondary_length_tree_prev_len;
		private readonly uint[] dec_last_matchpos_offset;

		private int dec_bufpos;
		private uint dec_current_file_size;
		private uint dec_instr_pos;
		private uint dec_num_cfdata_frames;

		private DecoderState dec_decoder_state;
		private int dec_block_size;
		private int dec_original_block_size;

		private BlockType dec_block_type;
		private uint dec_bitbuf;
		private sbyte dec_bitcount;

		private byte dec_num_position_slots;
		private bool dec_first_time_this_group;

		private bool dec_error_condition;

		private byte[] dec_input_buffer;
		private int dec_input_curpos;
		private int dec_end_input_pos;

		private byte[] dec_output_buffer;

		private byte[] dec_aligned_table;
		private byte[] dec_aligned_len;

		private short[] dec_main_tree_table;
		private short[] dec_main_tree_left_right;
		private short[] dec_secondary_length_tree_table;
		private short[] dec_secondary_length_tree_left_right;

		private int MAIN_TREE_ELEMENTS => NUM_CHARS + (dec_num_position_slots << NL_SHIFT);

		public LZXDeflate(int compression_window_size)
		{
			Build_global_tables();
			dec_window_size = compression_window_size;
			dec_window_mask = dec_window_size - 1;
			if ((dec_window_size & dec_window_mask) == 0 && allocate_decompression_memory())
			{
				dec_main_tree_len = new byte[MAX_MAIN_TREE_ELEMENTS];
				dec_main_tree_prev_len = new byte[MAX_MAIN_TREE_ELEMENTS];
				dec_secondary_length_tree_len = new byte[NUM_SECONDARY_LENGTHS];
				dec_secondary_length_tree_prev_len = new byte[NUM_SECONDARY_LENGTHS];
				dec_aligned_table = new byte[1 << ALIGNED_TABLE_BITS];
				dec_aligned_len = new byte[ALIGNED_NUM_ELEMENTS];
				dec_last_matchpos_offset = new uint[NUM_REPEATED_OFFSETS];
				dec_main_tree_table = new short[1 << MAIN_TREE_TABLE_BITS];
				dec_main_tree_left_right = new short[MAX_MAIN_TREE_ELEMENTS * 4];
				dec_secondary_length_tree_table = new short[1 << SECONDARY_LEN_TREE_TABLE_BITS];
				dec_secondary_length_tree_left_right = new short[NUM_SECONDARY_LENGTHS * 4];
				DecodeNewGroup();
			}
		}

		private bool allocate_decompression_memory()
		{
			dec_num_position_slots = 4;
			uint pos_start = 4;
			do
			{
				pos_start += (uint)(1 << dec_extra_bits[dec_num_position_slots]);
				dec_num_position_slots++;
			}
			while (pos_start < dec_window_size);

			dec_mem_window = new byte[dec_window_size + (MAX_MATCH + 4)];
			return true;
		}

		private static void ZeroArray(ref byte[] array)
		{
			Array.Clear(array, 0, array.Length);
		}

		private void DecodeNewGroup()
		{
			ZeroArray(ref dec_main_tree_len);
			ZeroArray(ref dec_main_tree_prev_len);
			ZeroArray(ref dec_secondary_length_tree_len);
			ZeroArray(ref dec_secondary_length_tree_prev_len);
			dec_last_matchpos_offset[0] = 1u;
			dec_last_matchpos_offset[1] = 1u;
			dec_last_matchpos_offset[2] = 1u;

			dec_bufpos = 0;

			dec_decoder_state = DecoderState.StartNewBlock;
			dec_block_size = 0;
			dec_original_block_size = 0;

			dec_block_type = BlockType.Invalid;

			dec_first_time_this_group = true;
			dec_current_file_size = 0u;

			dec_error_condition = false;

			dec_instr_pos = 0u;
			dec_num_cfdata_frames = 0u;
		}

		private void Build_global_tables()
		{
			MP_POS_minus2 = new int[51];
			int[] mP_POS_minus2_table = MP_POS_minus2;
			if (BitConverter.IsLittleEndian)
			{
				dec_extra_bits = new byte[] {
					0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
					7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14,
					15, 15, 0x10, 0x10, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
					0x11, 0x11, 0x11, 0x11
				};
			}
			else
			{
				dec_extra_bits = new byte[] {
					0, 0, 0, 0, 2, 2, 1, 1, 4, 4, 3, 3, 6, 6, 5, 5,
					8, 8, 7, 7, 10, 10, 9, 9, 12, 12, 11, 11, 14, 14, 13, 13,
					0x10, 0x10, 15, 15, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
					0x11, 0x11, 0x11, 0x11
				};
			}
			mP_POS_minus2_table[0] = -2;
			mP_POS_minus2_table[1] = -1;
			mP_POS_minus2_table[2] = 0;
			mP_POS_minus2_table[3] = 1;
			mP_POS_minus2_table[4] = 2;
			mP_POS_minus2_table[5] = 4;
			mP_POS_minus2_table[6] = 6;
			mP_POS_minus2_table[7] = 10;
			mP_POS_minus2_table[8] = 14;
			mP_POS_minus2_table[9] = 22;
			mP_POS_minus2_table[10] = 30;
			mP_POS_minus2_table[11] = 46;
			mP_POS_minus2_table[12] = 62;
			mP_POS_minus2_table[13] = 94;
			mP_POS_minus2_table[14] = 126;
			mP_POS_minus2_table[15] = 190;
			mP_POS_minus2_table[16] = 254;
			mP_POS_minus2_table[17] = 382;
			mP_POS_minus2_table[18] = 510;
			mP_POS_minus2_table[19] = 766;
			mP_POS_minus2_table[20] = 1022;
			mP_POS_minus2_table[21] = 1534;
			mP_POS_minus2_table[22] = 2046;
			mP_POS_minus2_table[23] = 3070;
			mP_POS_minus2_table[24] = 4094;
			mP_POS_minus2_table[25] = 6142;
			mP_POS_minus2_table[26] = 8190;
			mP_POS_minus2_table[27] = 12286;
			mP_POS_minus2_table[28] = 16382;
			mP_POS_minus2_table[29] = 24574;
			mP_POS_minus2_table[30] = 32766;
			mP_POS_minus2_table[31] = 49150;
			mP_POS_minus2_table[32] = 65534;
			mP_POS_minus2_table[33] = 98302;
			mP_POS_minus2_table[34] = 131070;
			mP_POS_minus2_table[35] = 196606;
			mP_POS_minus2_table[36] = 262142;
			mP_POS_minus2_table[37] = 393214;
			mP_POS_minus2_table[38] = 524286;
			mP_POS_minus2_table[39] = 655358;
			mP_POS_minus2_table[40] = 786430;
			mP_POS_minus2_table[41] = 917502;
			mP_POS_minus2_table[42] = 1048574;
			mP_POS_minus2_table[43] = 1179646;
			mP_POS_minus2_table[44] = 1310718;
			mP_POS_minus2_table[45] = 1441790;
			mP_POS_minus2_table[46] = 1572862;
			mP_POS_minus2_table[47] = 1703934;
			mP_POS_minus2_table[48] = 1835006;
			mP_POS_minus2_table[49] = 1966078;
			mP_POS_minus2_table[50] = 2097150;
		}

		public bool Reset()
		{
			DecodeNewGroup();
			return true;
		}

		public int Decompress(ref byte[] src, int src_length, ref byte[] tg, int dst_length)
		{
			dec_input_buffer = src;
			dec_input_curpos = 0;
			dec_end_input_pos = src_length;
			dec_output_buffer = tg;
			initialise_decoder_bitbuf();
			int result = decode_data(dst_length);
			dec_num_cfdata_frames++;
			return result;
		}

		private int decode_data(int bytes_to_decode)
		{
			int total_decoded = 0;
			while (bytes_to_decode > 0)
			{
				if (dec_decoder_state == DecoderState.StartNewBlock)
				{
					if (dec_first_time_this_group)
					{
						dec_first_time_this_group = false;
						if (getbits(1) != 0)
						{
							uint high = getbits(16);
							uint low = getbits(16);
							dec_current_file_size = (high << 16) | low;
						}
						else
						{
							dec_current_file_size = 0u;
						}
					}

					if (dec_block_type == BlockType.Uncompressed)
					{
						if ((dec_original_block_size & 1) > 0)
						{
							if (dec_input_curpos < dec_end_input_pos)
							{
								dec_input_curpos++;
							}
						}

						dec_block_type = BlockType.Invalid;

						initialise_decoder_bitbuf();
					}

					dec_block_type = (BlockType)getbits(3);

					uint temp1 = getbits(8);
					uint temp2 = getbits(8);
					uint temp3 = getbits(8);

					dec_block_size = (int)((temp1 << 16) + (temp2 << 8) + temp3);
					dec_original_block_size = dec_block_size;

					if (dec_block_type == BlockType.Aligned)
					{
						read_aligned_offset_tree();
					}

					if (dec_block_type == BlockType.Verbatim ||
						dec_block_type == BlockType.Aligned)
					{
						Buffer.BlockCopy(dec_main_tree_len, 0, dec_main_tree_prev_len, 0, MAIN_TREE_ELEMENTS);

						Buffer.BlockCopy(dec_secondary_length_tree_len, 0, dec_secondary_length_tree_prev_len, 0, NUM_SECONDARY_LENGTHS);

						read_main_and_secondary_trees();
					}
					else
					{
						if (dec_block_type != BlockType.Uncompressed)
						{
							return -1;
						}

						if (!handle_beginning_of_uncompressed_block())
						{
							return -1;
						}
					}

					dec_decoder_state = DecoderState.DecodingData;
				}
				while (dec_block_size > 0 && bytes_to_decode > 0)
				{
					uint amount_can_decode = (uint)Math.Min(dec_block_size, bytes_to_decode);
					if (amount_can_decode == 0)
					{
						return -1;
					}

					if (decode_block(dec_block_type, dec_bufpos, amount_can_decode) != 0)
					{
						return -1;
					}

					dec_block_size -= (int)amount_can_decode;
					bytes_to_decode -= (int)amount_can_decode;
					total_decoded += (int)amount_can_decode;
				}

				if (dec_block_size == 0)
				{
					dec_decoder_state = DecoderState.StartNewBlock;
				}

				if (bytes_to_decode == 0)
				{
					initialise_decoder_bitbuf();
				}
			}

			if (dec_bufpos > 0)
			{
				Buffer.BlockCopy(dec_mem_window, dec_bufpos - total_decoded, dec_output_buffer, 0, total_decoded);
			}
			else
			{
				Buffer.BlockCopy(dec_mem_window, dec_window_size - total_decoded, dec_output_buffer, 0, total_decoded);
			}

			if (dec_current_file_size != 0 && dec_num_cfdata_frames < E8_CFDATA_FRAME_THRESHOLD)
			{
				decoder_translate_e8(ref dec_output_buffer, total_decoded);
			}

			return total_decoded;
		}

		private void fillbuf(int n)
		{
			dec_bitbuf <<= n;
			dec_bitcount = (sbyte)(dec_bitcount - n);

			if (dec_bitcount > 0)
			{
				return;
			}

			if (dec_input_curpos + 2 > dec_end_input_pos)
			{
				dec_error_condition = true;
				return;
			}

			byte b = dec_input_buffer[dec_input_curpos++];
			byte b2 = dec_input_buffer[dec_input_curpos++];
			dec_bitbuf |= (uint)((b | (b2 << 8)) << -dec_bitcount);
			dec_bitcount += 16;

			if (dec_bitcount <= 0)
			{
				if (dec_input_curpos + 2 > dec_end_input_pos)
				{
					dec_error_condition = true;
					return;
				}

				b = dec_input_buffer[dec_input_curpos++];
				b2 = dec_input_buffer[dec_input_curpos++];
				dec_bitbuf |= (uint)((b | (b2 << 8)) << -dec_bitcount);
				dec_bitcount += 16;
			}
		}

		private uint getbits(int n)
		{
			uint result = dec_bitbuf >> (32 - n);
			fillbuf(n);
			return result;
		}

		private static bool make_table_8bit(ref byte[] bitlen, ref byte[] table)
		{
			ushort[] count = new ushort[17];
			ushort[] weight = new ushort[17];
			ushort[] start = new ushort[18];
			ushort i;

			for (i = 1; i <= 16; i++)
			{
				count[i] = 0;
			}

			for (i = 0; i < 8; i++)
			{
				count[bitlen[i]]++;
			}

			start[1] = 0;

			for (i = 1; i <= 16; i++)
			{
				start[i + 1] = (ushort)(start[i] + (count[i] << (16 - i)));
			}

			if (start[17] != 0)
			{
				return false;
			}

			for (i = 1; i <= 7; i++)
			{
				start[i] >>= 9;
				weight[i] = (ushort)(1 << 7 - i);
			}

			while (i <= 16)
			{
				weight[i] = (ushort)(1 << 16 - i);
				i++;
			}

			ZeroArray(ref table);

			for (byte ch = 0; ch < 8; ch++)
			{
				byte len;
				if ((len = bitlen[ch]) == 0)
				{
					continue;
				}

				ushort nextcode = (ushort)(start[len] + weight[len]);
				if (nextcode > (1 << 7))
				{
					return false;
				}

				for (i = start[len]; i < nextcode; i++)
				{
					table[i] = ch;
				}

				start[len] = nextcode;
			}

			return true;
		}

		private bool read_aligned_offset_tree()
		{
			for (int i = 0; i < 8; i++)
			{
				dec_aligned_len[i] = (byte)getbits(3);
			}

			if (dec_error_condition)
			{
				return false;
			}

			if (!make_table_8bit(ref dec_aligned_len, ref dec_aligned_table))
			{
				return false;
			}

			return true;
		}

		private static bool make_table(int nchar, ref byte[] bitlen, byte tablebits, ref short[] table, ref short[] leftright)
		{
			uint[] count = new uint[17];
			uint[] weight = new uint[17];
			uint[] start = new uint[18];
			uint i;

			for (i = 1u; i <= 16; i++)
			{
				count[i] = 0u;
			}

			for (i = 0u; i < (uint)nchar; i++)
			{
				count[bitlen[i]]++;
			}

			start[1] = 0u;

			for (i = 1u; i <= 16; i++)
			{
				start[i + 1] = start[i] + (count[i] << (byte)(16 - i));
			}

			if (start[17] != 65536)
			{
				if (start[17] == 0)
				{
					for (int y = 0; y < (uint)(1 << tablebits); y++)
					{
						table[y] = 0;
					}

					return true;
				}
				return false;
			}

			byte jutbits = (byte)(16 - tablebits);
			for (i = 1u; i <= tablebits; i++)
			{
				start[i] >>= jutbits;
				weight[i] = (uint)(1 << (byte)(tablebits - i));
			}
			for (; i <= 16; i++)
			{
				weight[i] = (uint)(1 << (byte)(16 - i));
			}

			i = start[tablebits + 1] >> jutbits;

			if (i != 65536)
			{
				for (uint y = 0u; y < (1 << tablebits) - i; y++)
				{
					table[i + y] = 0;
				}
			}

			int avail = nchar;
			for (int ch = 0; ch < nchar; ch++)
			{
				byte len;
				if ((len = bitlen[ch]) == 0)
				{
					continue;
				}

				uint nextcode = start[len] + weight[len];
				if (len <= tablebits)
				{
					if (nextcode > (uint)(1 << tablebits))
					{
						return false;
					}

					for (i = start[len]; i < nextcode; i++)
					{
						table[i] = (short)ch;
					}

					start[len] = nextcode;
					continue;
				}

				uint k = start[len];
				start[len] = nextcode;
				short[] p = table;
				int p_idx = (int)(k >> jutbits);

				i = (byte)(len - tablebits);
				k <<= tablebits;

				do
				{
					if (p[p_idx] == 0)
					{
						leftright[avail * 2] = leftright[avail * 2 + 1] = 0;
						p[p_idx] = (short)-avail;
						avail++;
					}
					if ((short)k < 0)
					{
						p_idx = -p[p_idx] * 2 + 1;
						p = leftright;
					}
					else
					{
						p_idx = -p[p_idx] * 2;
						p = leftright;
					}
					k <<= 1;
					i--;
				}
				while (i > 0);
				p[p_idx] = (short)ch;
			}

			return true;
		}

		private void DECODE_FILLBUF(int n)
		{
			dec_bitbuf <<= n;
			dec_bitcount -= (sbyte)n;
			if (dec_bitcount > 0)
			{
				return;
			}

			if (dec_input_curpos + 2 > dec_end_input_pos)
			{
				dec_error_condition = true;
				return;
			}

			dec_bitbuf |= (uint)((dec_input_buffer[dec_input_curpos] | (dec_input_buffer[dec_input_curpos + 1] << 8)) << -dec_bitcount);
			dec_input_curpos += 2;
			dec_bitcount += 16;

			if (dec_bitcount <= 0)
			{
				if (dec_input_curpos + 2 > dec_end_input_pos)
				{
					dec_error_condition = true;
					return;
				}

				dec_bitbuf |= (uint)((dec_input_buffer[dec_input_curpos] | (dec_input_buffer[dec_input_curpos + 1] << 8)) << -dec_bitcount);
				dec_input_curpos += 2;
				dec_bitcount += 16;
			}
		}

		private bool DECODE_SMALL(ref int small_table_idx, ref short[] small_table, ref uint mask, ref int leftright_idx, ref short[] leftright_s, ref byte[] small_bitlen, ref short item)
		{
			small_table_idx = (int)(dec_bitbuf >> (32 - DS_TABLE_BITS));

			if (small_table_idx >= small_table.Length)
			{
				dec_error_condition = true;
				return false;
			}

			item = small_table[small_table_idx];
			if (item < 0)
			{
				mask = 1 << (32 - 1 - DS_TABLE_BITS);
				do
				{
					item = (short)(-item);
					if ((dec_bitbuf & mask) != 0)
					{
						leftright_idx = 2 * item + 1;
					}
					else
					{
						leftright_idx = 2 * item;
					}

					if (leftright_idx >= leftright_s.Length)
					{
						dec_error_condition = true;
						return false;
					}

					item = leftright_s[leftright_idx];
					mask >>= 1;
				}
				while (item < 0);
			}

			if (item >= small_bitlen.Length)
			{
				dec_error_condition = true;
				return false;
			}

			DECODE_FILLBUF(small_bitlen[item]);
			return true;
		}

		private void DECODE_GETBITS(ref int dest, int n)
		{
			dest = (byte)(dec_bitbuf >> (32 - n));
			DECODE_FILLBUF(n);
		}

		private bool ReadRepTree(int num_elements, ref byte[] dec_tree_prev_len, int lastlen, ref byte[] dec_tree_len, int len)
		{
			uint mask = 0u;
			int consecutive = 0;
			byte[] small_bitlen = new byte[24];
			short[] small_table = new short[1 << DS_TABLE_BITS];
			short[] leftright_s = new short[2 * (2 * 24 - 1)];
			short Temp = 0;

			int small_table_idx = 0;
			int leftright_idx = 0;
			for (int i = 0; i < NUM_DECODE_SMALL; i++)
			{
				small_bitlen[i] = (byte)getbits(4);
			}

			if (dec_error_condition)
			{
				return false;
			}

			make_table(NUM_DECODE_SMALL, ref small_bitlen, DS_TABLE_BITS, ref small_table, ref leftright_s);

			for (int i = 0; i < num_elements; i++)
			{
				DECODE_SMALL(ref small_table_idx, ref small_table, ref mask, ref leftright_idx, ref leftright_s, ref small_bitlen, ref Temp);
				if (dec_error_condition)
				{
					break;
				}

				switch (Temp)
				{
					case 17:
						DECODE_GETBITS(ref consecutive, TREE_ENC_REPZ_FIRST_EXTRA_BITS);
						consecutive += TREE_ENC_REP_MIN;
						if (i + consecutive >= num_elements)
						{
							consecutive = num_elements - i;
						}

						while (consecutive-- > 0)
						{
							dec_tree_len[len + i] = 0;
							i++;
						}
						i--;
						break;
					case 18:
						DECODE_GETBITS(ref consecutive, TREE_ENC_REPZ_SECOND_EXTRA_BITS);
						consecutive += (TREE_ENC_REP_MIN + TREE_ENC_REP_ZERO_FIRST);
						if (i + consecutive >= num_elements)
						{
							consecutive = num_elements - i;
						}

						while (consecutive-- > 0)
						{
							dec_tree_len[len + i] = 0;
							i++;
						}
						i--;
						break;
					case 19:
						{
							DECODE_GETBITS(ref consecutive, TREE_ENC_REP_SAME_EXTRA_BITS);
							consecutive += TREE_ENC_REP_MIN;

							if (i + consecutive >= num_elements)
							{
								consecutive = num_elements - i;
							}

							DECODE_SMALL(ref small_table_idx, ref small_table, ref mask, ref leftright_idx, ref leftright_s, ref small_bitlen, ref Temp);
							int num = dec_tree_prev_len[lastlen + i] - Temp + 17;
							if (num >= 17)
							{
								num -= 17;
							}

							byte b = (byte)num;

							while (consecutive-- > 0)
							{
								dec_tree_len[len + i] = b;
								i++;
							}

							i--;
							break;
						}
					default:
						{
							int num = dec_tree_prev_len[lastlen + i] - Temp + 17;
							if (num >= 17)
							{
								num -= 17;
							}

							byte b = (byte)num;

							dec_tree_len[len + i] = b;
							break;
						}
				}
			}
			return !dec_error_condition;
		}

		private bool read_main_and_secondary_trees()
		{
			if (!ReadRepTree(256, ref dec_main_tree_prev_len, 0, ref dec_main_tree_len, 0))
			{
				return false;
			}
			if (!ReadRepTree(dec_num_position_slots * NUM_LENGTHS, ref dec_main_tree_prev_len, 256, ref dec_main_tree_len, 256))
			{
				return false;
			}
			if (!make_table(MAIN_TREE_ELEMENTS, ref dec_main_tree_len, MAIN_TREE_TABLE_BITS, ref dec_main_tree_table, ref dec_main_tree_left_right))
			{
				return false;
			}
			if (!ReadRepTree(NUM_SECONDARY_LENGTHS, ref dec_secondary_length_tree_prev_len, 0, ref dec_secondary_length_tree_len, 0))
			{
				return false;
			}
			if (!make_table(NUM_SECONDARY_LENGTHS, ref dec_secondary_length_tree_len, SECONDARY_LEN_TREE_TABLE_BITS, ref dec_secondary_length_tree_table, ref dec_secondary_length_tree_left_right))
			{
				return false;
			}
			return true;
		}

		private void decoder_translate_e8(ref byte[] mem, int bytes)
		{
			byte[] temp = new byte[6];

			if (bytes <= 6)
			{
				dec_instr_pos += (uint)bytes;
				return;
			}

			Buffer.BlockCopy(mem, bytes - 6, temp, 0, 6);
			mem[bytes - 6] = 0xE8;
			mem[bytes - 5] = 0xE8;
			mem[bytes - 4] = 0xE8;
			mem[bytes - 3] = 0xE8;
			mem[bytes - 2] = 0xE8;
			mem[bytes - 1] = 0xE8;

			uint end_instr_pos = (uint)(dec_instr_pos + bytes - 10);

			int offset = 0;
			while (true)
			{
				while (mem[offset++] != 0xE8)
					dec_instr_pos++;

				if (dec_instr_pos >= end_instr_pos)
					break;

				uint absolute = BitConverter.ToUInt32(mem, offset);
				if (absolute < dec_current_file_size)
				{
					byte[] bytes2 = BitConverter.GetBytes(absolute - dec_instr_pos);
					mem[offset] = bytes2[0];
					mem[offset + 1] = bytes2[1];
					mem[offset + 2] = bytes2[2];
					mem[offset + 3] = bytes2[3];
				}
				else if ((uint)(0 - (int)absolute) <= dec_instr_pos)
				{
					byte[] bytes2 = BitConverter.GetBytes(absolute + dec_current_file_size);
					mem[offset] = bytes2[0];
					mem[offset + 1] = bytes2[1];
					mem[offset + 2] = bytes2[2];
					mem[offset + 3] = bytes2[3];
				}

				offset += 4;
				dec_instr_pos += 5;
			}

			dec_instr_pos = end_instr_pos + 10;
			Buffer.BlockCopy(temp, 0, mem, bytes - 6, 6);
		}

		private int decode_block(BlockType block_type, int bufpos, uint amount_to_decode)
		{
			switch (block_type)
			{
				case BlockType.Aligned:
					return decode_aligned_offset_block(bufpos, (int)amount_to_decode);
				case BlockType.Verbatim:
					return decode_verbatim_block(bufpos, (int)amount_to_decode);
				case BlockType.Uncompressed:
					return decode_uncompressed_block(bufpos, (int)amount_to_decode);
				default:
					return -1;
			}
		}

		private int decode_aligned_offset_block(int bufpos, int amount_to_decode)
		{
			if (bufpos < MAX_MATCH)
			{
				int amount_to_slowly_decode = Math.Min(MAX_MATCH - bufpos, amount_to_decode);
				int new_bufpos = special_decode_aligned_block(bufpos, amount_to_slowly_decode);

				amount_to_decode -= new_bufpos - bufpos;

				dec_bufpos = bufpos = new_bufpos;

				if (amount_to_decode <= 0)
				{
					return amount_to_decode;
				}
			}
			return fast_decode_aligned_offset_block(bufpos, amount_to_decode);
		}

		private int special_decode_aligned_block(int bufpos, int amount_to_decode)
		{
			int bufpos_end = bufpos + amount_to_decode;
			while (bufpos < bufpos_end)
			{
				int c = DecodeMainTree();
				if ((c -= NUM_CHARS) < 0)
				{
					dec_mem_window[bufpos] = (byte)c;
					dec_mem_window[dec_window_size + bufpos] = (byte)c;
					bufpos++;
					continue;
				}
				int match_length = c & NUM_PRIMARY_LENGTHS;
				if (match_length == NUM_PRIMARY_LENGTHS)
				{
					DecodeLenTreeNoEofCheck(ref match_length);
				}
				sbyte m = (sbyte)(c >> NL_SHIFT);
				uint match_pos;
				if (m > 2)
				{
					if (dec_extra_bits[m] >= 3)
					{
						uint temp_pos = (dec_extra_bits[m] - 3 > 0) ? GetBitsNoEofCheck(dec_extra_bits[m] - 3) : 0u;
						match_pos = (uint)(MP_POS_minus2[m] + (int)(temp_pos << 3));
						temp_pos = DecodeAlignedNoEofCheck();
						match_pos += temp_pos;
					}
					else if (dec_extra_bits[m] > 0)
					{
						match_pos = GetBitsNoEofCheck(dec_extra_bits[m]);
						match_pos = (uint)((int)match_pos + MP_POS_minus2[m]);
					}
					else
					{
						match_pos = 1u;
					}
					dec_last_matchpos_offset[2] = dec_last_matchpos_offset[1];
					dec_last_matchpos_offset[1] = dec_last_matchpos_offset[0];
					dec_last_matchpos_offset[0] = match_pos;
				}
				else
				{
					match_pos = dec_last_matchpos_offset[m];
					dec_last_matchpos_offset[m] = dec_last_matchpos_offset[0];
					dec_last_matchpos_offset[0] = match_pos;
				}
				match_length += MIN_MATCH;
				do
				{
					uint num5 = dec_mem_window[(bufpos - match_pos) & dec_window_mask];
					dec_mem_window[bufpos] = (byte)num5;
					if (bufpos < MAX_MATCH)
					{
						dec_mem_window[dec_window_size + bufpos] = (byte)num5;
					}

					bufpos++;
				}
				while (--match_length > 0);
			}

			return bufpos;
		}

		private int fast_decode_aligned_offset_block(int bufpos, int amount_to_decode)
		{
			int bufpos_end = bufpos + amount_to_decode;
			while (bufpos < bufpos_end)
			{
				int c = DecodeMainTree();
				if ((c -= NUM_CHARS) < 0)
				{
					dec_mem_window[bufpos++] = (byte)c;
					continue;
				}
				int match_length = c & NUM_PRIMARY_LENGTHS;
				if (match_length == NUM_PRIMARY_LENGTHS)
				{
					DecodeLenTreeNoEofCheck(ref match_length);
				}
				sbyte m = (sbyte)(c >> NL_SHIFT);
				uint match_pos;
				if (m > 2)
				{
					if (dec_extra_bits[m] >= 3)
					{
						uint temp_pos = (dec_extra_bits[m] - 3 > 0) ? GetBitsNoEofCheck(dec_extra_bits[m] - 3) : 0u;
						match_pos = (uint)(MP_POS_minus2[m] + (temp_pos << 3));
						temp_pos = DecodeAlignedNoEofCheck();
						match_pos += temp_pos;
					}
					else
					{
						if (dec_extra_bits[m] > 0)
						{
							match_pos = GetBitsNoEofCheck(dec_extra_bits[m]);
							match_pos = (uint)((int)match_pos + MP_POS_minus2[m]);
						}
						else
						{
							match_pos = (uint)MP_POS_minus2[m];
						}
					}
					dec_last_matchpos_offset[2] = dec_last_matchpos_offset[1];
					dec_last_matchpos_offset[1] = dec_last_matchpos_offset[0];
					dec_last_matchpos_offset[0] = match_pos;
				}
				else
				{
					match_pos = dec_last_matchpos_offset[m];
					if (m > 0)
					{
						dec_last_matchpos_offset[m] = dec_last_matchpos_offset[0];
						dec_last_matchpos_offset[0] = match_pos;
					}
				}
				match_length += MIN_MATCH;
				uint match_ptr = (uint)((bufpos - (int)match_pos) & dec_window_mask);
				do
				{
					dec_mem_window[bufpos++] = dec_mem_window[match_ptr++];
				}
				while (--match_length > 0);
			}

			int decode_residue = bufpos - bufpos_end;
			bufpos &= dec_window_mask;
			dec_bufpos = bufpos;
			return decode_residue;
		}

		private int decode_verbatim_block(int bufpos, int amount_to_decode)
		{
			if (bufpos < MAX_MATCH)
			{
				int amount_to_slowly_decode = Math.Min(MAX_MATCH - bufpos, amount_to_decode);
				int new_bufpos = special_decode_verbatim_block(bufpos, amount_to_slowly_decode);
				amount_to_decode -= (new_bufpos - bufpos);
				dec_bufpos = bufpos = new_bufpos;

				if (amount_to_decode <= 0)
				{
					return amount_to_decode;
				}
			}
			return fast_decode_verbatim_block(bufpos, amount_to_decode);
		}

		private int special_decode_verbatim_block(int bufpos, int amount_to_decode)
		{
			int bufpos_end = bufpos + amount_to_decode;
			while (bufpos < bufpos_end)
			{
				int c = DecodeMainTree();
				if ((c -= 256) < 0)
				{
					dec_mem_window[bufpos] = (byte)c;
					dec_mem_window[dec_window_size + bufpos] = (byte)c;
					bufpos++;
					continue;
				}
				int match_length = c & NUM_PRIMARY_LENGTHS;
				if (match_length == NUM_PRIMARY_LENGTHS)
				{
					DecodeLenTreeNoEofCheck(ref match_length);
				}
				sbyte m = (sbyte)(c >> NL_SHIFT);
				uint match_pos;
				if (m > 2)
				{
					if (m > 3)
					{
						match_pos = GetBits17NoEofCheck(dec_extra_bits[m]);
						match_pos = (uint)((int)match_pos + MP_POS_minus2[m]);
					}
					else
					{
						match_pos = 1u;
					}
					dec_last_matchpos_offset[2] = dec_last_matchpos_offset[1];
					dec_last_matchpos_offset[1] = dec_last_matchpos_offset[0];
					dec_last_matchpos_offset[0] = match_pos;
				}
				else
				{
					match_pos = dec_last_matchpos_offset[m];
					if (m > 0)
					{
						dec_last_matchpos_offset[m] = dec_last_matchpos_offset[0];
						dec_last_matchpos_offset[0] = match_pos;
					}
				}
				match_length += MIN_MATCH;
				do
				{
					dec_mem_window[bufpos] = dec_mem_window[(bufpos - match_pos) & dec_window_mask];
					if (bufpos < MAX_MATCH)
					{
						dec_mem_window[dec_window_size + bufpos] = dec_mem_window[bufpos];
					}
					bufpos++;
				}
				while (--match_length > 0);
			}

			return bufpos;
		}

		private int fast_decode_verbatim_block(int bufpos, int amount_to_decode)
		{
			int bufpos_end = bufpos + amount_to_decode;
			while (bufpos < bufpos_end)
			{
				int c = DecodeMainTree();
				if ((c -= NUM_CHARS) < 0)
				{
					dec_mem_window[bufpos++] = (byte)c;
					continue;
				}
				int match_length = c & NUM_PRIMARY_LENGTHS;
				if (match_length == NUM_PRIMARY_LENGTHS)
				{
					DecodeLenTreeNoEofCheck(ref match_length);
				}
				sbyte m = (sbyte)(c >> NL_SHIFT);
				uint match_pos;
				if (m > 2)
				{
					if (m > 3)
					{
						match_pos = GetBits17NoEofCheck(dec_extra_bits[m]);
						match_pos = (uint)((int)match_pos + MP_POS_minus2[m]);
					}
					else
					{
						match_pos = (uint)MP_POS_minus2[3];
					}
					dec_last_matchpos_offset[2] = dec_last_matchpos_offset[1];
					dec_last_matchpos_offset[1] = dec_last_matchpos_offset[0];
					dec_last_matchpos_offset[0] = match_pos;
				}
				else
				{
					match_pos = dec_last_matchpos_offset[m];
					if (m > 0)
					{
						dec_last_matchpos_offset[m] = dec_last_matchpos_offset[0];
						dec_last_matchpos_offset[0] = match_pos;
					}
				}
				match_length += MIN_MATCH;

				uint match_ptr = (uint)((bufpos - (int)match_pos) & dec_window_mask);
				do
				{
					dec_mem_window[bufpos++] = dec_mem_window[match_ptr++];
				}
				while (--match_length > 0);
			}

			int decode_residue = bufpos - bufpos_end;

			bufpos &= dec_window_mask;
			dec_bufpos = bufpos;

			return decode_residue;
		}

		private int decode_uncompressed_block(int bufpos, int amount_to_decode)
		{
			int bufpos_start = bufpos;
			int bufpos_end = bufpos + amount_to_decode;

			while (bufpos < bufpos_end)
			{
				if (dec_input_curpos + 1 > dec_end_input_pos)
				{
					return -1; // input overflow
				}

				dec_mem_window[bufpos++] = dec_input_buffer[dec_input_curpos++];
			}

			int end_copy_pos = Math.Min(MAX_MATCH, bufpos_end);
			while (bufpos_start < end_copy_pos)
			{
				dec_mem_window[bufpos_start + dec_window_size] = dec_mem_window[bufpos_start];
				bufpos_start++;
			}

			int decode_residue = bufpos - bufpos_end;
			bufpos &= dec_window_mask;
			dec_bufpos = bufpos;

			return decode_residue;
		}

		private bool handle_beginning_of_uncompressed_block()
		{
			dec_input_curpos -= 2;
			if (dec_input_curpos + 4 > dec_end_input_pos)
			{
				return false;
			}

			for (int i = 0; i < NUM_REPEATED_OFFSETS; i++)
			{
				byte b = dec_input_buffer[dec_input_curpos++];
				byte b2 = dec_input_buffer[dec_input_curpos++];
				byte b3 = dec_input_buffer[dec_input_curpos++];
				byte b4 = dec_input_buffer[dec_input_curpos++];
				dec_last_matchpos_offset[i] = (uint)(b | (b2 << 8) | (b3 << 16) | (b4 << 24));
			}
			return true;
		}

		private uint DecodeAlignedNoEofCheck()
		{
			uint num = dec_aligned_table[dec_bitbuf >> (32 - ALIGNED_TABLE_BITS)];
			FillBufNoEofCheck(dec_aligned_len[num]);
			return num;
		}

		private void initialise_decoder_bitbuf()
		{
			if (dec_block_type != BlockType.Uncompressed && dec_input_curpos + 4 <= dec_end_input_pos)
			{
				byte b = dec_input_buffer[dec_input_curpos++];
				byte b2 = dec_input_buffer[dec_input_curpos++];
				byte b3 = dec_input_buffer[dec_input_curpos++];
				byte b4 = dec_input_buffer[dec_input_curpos++];
				dec_bitbuf = (uint)(b3 | (b4 << 8) | ((b | (b2 << 8)) << 16));
				dec_bitcount = 16;
			}
		}

		private void FillBufFillCheck(int N)
		{
			if (dec_input_curpos <= dec_end_input_pos)
			{
				dec_bitbuf <<= N;
				dec_bitcount = (sbyte)(dec_bitcount - N);
				if (dec_bitcount <= 0 && dec_input_curpos + 2 <= dec_end_input_pos)
				{
					byte b = dec_input_buffer[dec_input_curpos++];
					byte b2 = dec_input_buffer[dec_input_curpos++];
					dec_bitbuf |= (uint)((b | (b2 << 8)) << -dec_bitcount);
					dec_bitcount += 16;
				}
			}
		}

		private void FillBufNoEofCheck(int N)
		{
			dec_bitbuf <<= N;
			dec_bitcount = (sbyte)(dec_bitcount - N);
			if (dec_bitcount <= 0 && dec_input_curpos + 2 <= dec_end_input_pos)
			{
				byte b = dec_input_buffer[dec_input_curpos++];
				byte b2 = dec_input_buffer[dec_input_curpos++];
				dec_bitbuf |= (uint)((b | (b2 << 8)) << -dec_bitcount);
				dec_bitcount += 16;
			}
		}

		private void FillBuf17NoEofCheck(int N)
		{
			dec_bitbuf <<= N;
			dec_bitcount = (sbyte)(dec_bitcount - N);
			if (dec_bitcount <= 0 && dec_input_curpos + 2 <= dec_end_input_pos)
			{
				byte b = dec_input_buffer[dec_input_curpos++];
				byte b2 = dec_input_buffer[dec_input_curpos++];
				dec_bitbuf |= (uint)((b | (b2 << 8)) << -dec_bitcount);
				dec_bitcount += 16;
				if (dec_bitcount <= 0 && dec_input_curpos + 2 <= dec_end_input_pos)
				{
					b = dec_input_buffer[dec_input_curpos++];
					b2 = dec_input_buffer[dec_input_curpos++];
					dec_bitbuf |= (uint)((b | (b2 << 8)) << -dec_bitcount);
					dec_bitcount += 16;
				}
			}
		}

		private int DecodeMainTree()
		{
			int j = dec_main_tree_table[dec_bitbuf >> (32 - MAIN_TREE_TABLE_BITS)];
			if (j < 0)
			{
				uint mask = (uint)(1L << (32 - 1 - MAIN_TREE_TABLE_BITS));
				do
				{
					j = -j;
					if ((dec_bitbuf & mask) != 0)
					{
						j = dec_main_tree_left_right[j * 2 + 1];
					}
					else
					{
						j = dec_main_tree_left_right[j * 2];
					}

					mask >>= 1;
				}
				while (j < 0);
			}
			FillBufFillCheck(dec_main_tree_len[j]);
			return j;
		}

		private void DecodeLenTreeNoEofCheck(ref int matchlen)
		{
			matchlen = dec_secondary_length_tree_table[dec_bitbuf >> (32 - SECONDARY_LEN_TREE_TABLE_BITS)];
			if (matchlen < 0)
			{
				uint mask = (uint)(1L << (32 - 1 - SECONDARY_LEN_TREE_TABLE_BITS));
				do
				{
					matchlen = -matchlen;
					if ((dec_bitbuf & mask) != 0)
					{
						matchlen = dec_secondary_length_tree_left_right[matchlen * 2 + 1];
					}
					else
					{
						matchlen = dec_secondary_length_tree_left_right[matchlen * 2];
					}
					mask >>= 1;
				}
				while (matchlen < 0);
			}
			FillBufNoEofCheck(dec_secondary_length_tree_len[matchlen]);
			matchlen += NUM_PRIMARY_LENGTHS;
		}

		private uint GetBitsNoEofCheck(int N)
		{
			uint result = dec_bitbuf >> (32 - N);
			FillBufNoEofCheck(N);
			return result;
		}

		private uint GetBits17NoEofCheck(int N)
		{
			uint result = dec_bitbuf >> (32 - N);
			FillBuf17NoEofCheck(N);
			return result;
		}

		public enum BlockType
		{
			Invalid,
			Verbatim,
			Aligned,
			Uncompressed
		}

		public enum DecoderState
		{
			Unknown,
			StartNewBlock,
			DecodingData
		}

		private const int MIN_MATCH = 2;
		private const int MAX_MATCH = 257;
		private const int NUM_CHARS = 256;
		private const int NUM_PRIMARY_LENGTHS = 7;
		private const int NUM_LENGTHS = 8;
		private const int NUM_SECONDARY_LENGTHS = 249;
		private const int NL_SHIFT = 3;
		private const int NUM_REPEATED_OFFSETS = 3;
		private const int ALIGNED_NUM_ELEMENTS = 8;
		private const int TREE_ENC_REP_MIN = 4;
		private const int TREE_ENC_REP_ZERO_FIRST = 16;
		private const int TREE_ENC_REPZ_FIRST_EXTRA_BITS = 4;
		private const int TREE_ENC_REPZ_SECOND_EXTRA_BITS = 5;
		private const int TREE_ENC_REP_SAME_EXTRA_BITS = 1;
		private const int E8_CFDATA_FRAME_THRESHOLD = 32768;
		private const int CHUNK_SIZE = 0x8000;
		private const int MAX_GROWTH = 6144;
		private const int DS_TABLE_BITS = 8;
		private const int MAX_MAIN_TREE_ELEMENTS = 672;
		private const int MAIN_TREE_TABLE_BITS = 10;
		private const int SECONDARY_LEN_TREE_TABLE_BITS = 8;
		private const int ALIGNED_TABLE_BITS = 7;
		private const int NUM_DECODE_SMALL = 20;

		public const int MAX_COMPRESSED_BLOCK_SIZE = 0x980A;
		public const int MAX_DECOMPRESSED_BLOCK_SIZE = CHUNK_SIZE + MAX_GROWTH;

		// Unused, maybe for encoding?
		private const int TREE_ENC_REP_ZERO_SECOND = 32;
		private const int TREE_ENC_REP_SAME_FIRST = 2;
		private const int MAX_WINDOW_SIZE = 2097152;
		private const int MAX_PARTITION_SIZE = 16777216;
		private const int LZXMARK_END_OF_STREAM = 255;
		private const int DECODER_PREFETCH_PAD_SIZE = 5;
		private const int TDECODE_STAGING_BUFFER_SIZE = 32768;
	}
}
